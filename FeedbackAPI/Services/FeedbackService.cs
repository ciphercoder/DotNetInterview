using FeedbackAPI.Data;
using FeedbackAPI.DTOs;
using FeedbackAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace FeedbackAPI.Services;

/// <summary>
/// Orchestrates four data stores in one service:
///   SQL (EF Core)   – primary source of truth, CRUD
///   Redis           – dashboard cache (summary + trends)   [read-heavy]
///   MongoDB         – immutable audit log of every change  [write-heavy append]
///   Elasticsearch   – full-text search index               [separate read path]
///
/// Failure policy: Redis / MongoDB / ES failures are caught and logged.
/// They NEVER propagate to the caller – the SQL operation always wins.
/// </summary>
public class FeedbackService(
    FeedbackDbContext db,
    ICacheService     cache,
    IAuditService     audit,
    ISearchService    search) : IFeedbackService
{
    // ── Cache key helpers ────────────────────────────────────────────────────
    // Keys are deterministic: same filter params → same key every time.

    private static string SummaryCacheKey(FeedbackFilterParams f) =>
        $"feedback:summary:{f.Category}:{f.Sentiment}:{f.MinRating}:{f.MaxRating}" +
        $":{f.From:yyyyMMdd}:{f.To:yyyyMMdd}:{f.Region}:{f.ProductId}:{f.IsResolved}";

    private static string TrendsCacheKey(DateTime from, DateTime to, string groupBy) =>
        $"feedback:trends:{from:yyyyMMdd}:{to:yyyyMMdd}:{groupBy}";

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static SentimentType InferSentiment(int rating) => rating switch
    {
        >= 4 => SentimentType.Positive,
        3    => SentimentType.Neutral,
        _    => SentimentType.Negative
    };

    private static FeedbackResponseDto ToDto(CustomerFeedback f) => new()
    {
        Id           = f.Id,
        CustomerName = f.CustomerName,
        CustomerEmail = f.CustomerEmail,
        Category     = f.Category.ToString(),
        Sentiment    = f.Sentiment.ToString(),
        Rating       = f.Rating,
        Comment      = f.Comment,
        CreatedAt    = f.CreatedAt,
        ProductId    = f.ProductId,
        Region       = f.Region,
        IsResolved   = f.IsResolved
    };

    private IQueryable<CustomerFeedback> ApplyFilters(IQueryable<CustomerFeedback> query,
                                                       FeedbackFilterParams filter)
    {
        if (filter.From.HasValue)       query = query.Where(f => f.CreatedAt >= filter.From.Value);
        if (filter.To.HasValue)         query = query.Where(f => f.CreatedAt <= filter.To.Value);
        if (filter.Category.HasValue)   query = query.Where(f => f.Category == filter.Category.Value);
        if (filter.Sentiment.HasValue)  query = query.Where(f => f.Sentiment == filter.Sentiment.Value);
        if (filter.MinRating.HasValue)  query = query.Where(f => f.Rating >= filter.MinRating.Value);
        if (filter.MaxRating.HasValue)  query = query.Where(f => f.Rating <= filter.MaxRating.Value);
        if (filter.IsResolved.HasValue) query = query.Where(f => f.IsResolved == filter.IsResolved.Value);

        if (!string.IsNullOrWhiteSpace(filter.Region))
            query = query.Where(f => f.Region == filter.Region);

        if (!string.IsNullOrWhiteSpace(filter.ProductId))
            query = query.Where(f => f.ProductId == filter.ProductId);

        return query;
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────

    public async Task<PagedResult<FeedbackResponseDto>> GetAllAsync(FeedbackFilterParams filter)
    {
        var query = ApplyFilters(db.Feedbacks.AsNoTracking(), filter)
                        .OrderByDescending(f => f.CreatedAt);

        var total = await query.CountAsync();
        var data = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(f => ToDto(f))
            .ToListAsync();

        return new PagedResult<FeedbackResponseDto>
        {
            Data      = data,
            TotalCount = total,
            Page      = filter.Page,
            PageSize  = filter.PageSize
        };
    }

    public async Task<FeedbackResponseDto?> GetByIdAsync(int id)
    {
        var entity = await db.Feedbacks.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<FeedbackResponseDto> CreateAsync(FeedbackCreateDto dto)
    {
        var rating = Math.Clamp(dto.Rating, 1, 5);
        var entity = new CustomerFeedback
        {
            CustomerName  = dto.CustomerName.Trim(),
            CustomerEmail = dto.CustomerEmail?.Trim(),
            Category      = dto.Category,
            Sentiment     = InferSentiment(rating),
            Rating        = rating,
            Comment       = dto.Comment.Trim(),
            ProductId     = dto.ProductId?.Trim(),
            Region        = dto.Region?.Trim(),
            CreatedAt     = DateTime.UtcNow
        };

        // ① SQL – primary write (must succeed)
        db.Feedbacks.Add(entity);
        await db.SaveChangesAsync();

        var result = ToDto(entity);

        // ② Fan-out to secondary stores in parallel (failures are swallowed internally)
        await Task.WhenAll(
            search.IndexAsync(result),                              // ES: index for full-text search
            audit.LogCreatedAsync(entity.Id, result),              // MongoDB: audit trail
            cache.RemoveAsync(SummaryCacheKey(new()))              // Redis: bust the no-filter summary
        );

        return result;
    }

    public async Task<FeedbackResponseDto?> UpdateAsync(int id, FeedbackUpdateDto dto)
    {
        var entity = await db.Feedbacks.FindAsync(id);
        if (entity is null) return null;

        // Capture BEFORE state for MongoDB audit trail
        var before = ToDto(entity);

        if (dto.Category.HasValue)  entity.Category   = dto.Category.Value;
        if (dto.IsResolved.HasValue) entity.IsResolved = dto.IsResolved.Value;
        if (dto.Comment is not null) entity.Comment   = dto.Comment.Trim();

        if (dto.Rating.HasValue)
        {
            entity.Rating    = Math.Clamp(dto.Rating.Value, 1, 5);
            entity.Sentiment = InferSentiment(entity.Rating);
        }

        // ① SQL – primary write
        await db.SaveChangesAsync();

        var after = ToDto(entity);

        // ② Fan-out: re-index in ES, record in MongoDB, bust cache
        await Task.WhenAll(
            search.IndexAsync(after),
            audit.LogUpdatedAsync(entity.Id, before, after),
            cache.RemoveAsync(SummaryCacheKey(new()))
        );

        return after;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await db.Feedbacks.FindAsync(id);
        if (entity is null) return false;

        // Capture snapshot before deletion so audit log has the full record
        var snapshot = ToDto(entity);

        // ① SQL – primary delete
        db.Feedbacks.Remove(entity);
        await db.SaveChangesAsync();

        // ② Fan-out: remove from ES, record in MongoDB, bust cache
        await Task.WhenAll(
            search.DeleteAsync(id),
            audit.LogDeletedAsync(id, snapshot),
            cache.RemoveAsync(SummaryCacheKey(new()))
        );

        return true;
    }

    // ── Aggregations ─────────────────────────────────────────────────────────

    public async Task<FeedbackSummaryDto> GetSummaryAsync(FeedbackFilterParams filter)
    {
        var cacheKey = SummaryCacheKey(filter);

        // ① Redis: try cache first (cache-aside pattern)
        var cached = await cache.GetAsync<FeedbackSummaryDto>(cacheKey);
        if (cached is not null) return cached;

        // ② SQL: cache miss → compute from database
        var result = await ComputeSummaryAsync(filter);

        // ③ Redis: store result for 60 seconds
        await cache.SetAsync(cacheKey, result, TimeSpan.FromSeconds(60));

        return result;
    }

    private async Task<FeedbackSummaryDto> ComputeSummaryAsync(FeedbackFilterParams filter)
    {
        var query = ApplyFilters(db.Feedbacks.AsNoTracking(), filter);
        var all   = await query.ToListAsync();

        if (all.Count == 0)
            return new FeedbackSummaryDto();

        return new FeedbackSummaryDto
        {
            TotalCount    = all.Count,
            AverageRating = all.Average(f => f.Rating),
            PositiveCount = all.Count(f => f.Sentiment == SentimentType.Positive),
            NeutralCount  = all.Count(f => f.Sentiment == SentimentType.Neutral),
            NegativeCount = all.Count(f => f.Sentiment == SentimentType.Negative),
            ResolvedCount = all.Count(f => f.IsResolved),
            CountByCategory = all.GroupBy(f => f.Category.ToString())
                                  .ToDictionary(g => g.Key, g => g.Count()),
            CountByRegion = all.Where(f => f.Region != null)
                               .GroupBy(f => f.Region!)
                               .ToDictionary(g => g.Key, g => g.Count()),
            AvgRatingByCategory = all.GroupBy(f => f.Category.ToString())
                                     .ToDictionary(g => g.Key, g => g.Average(f => f.Rating))
        };
    }

    public async Task<IEnumerable<TrendDataPointDto>> GetTrendsAsync(
        DateTime from, DateTime to, string groupBy)
    {
        var cacheKey = TrendsCacheKey(from, to, groupBy);

        // ① Redis: try cache first
        var cached = await cache.GetAsync<List<TrendDataPointDto>>(cacheKey);
        if (cached is not null) return cached;

        // ② SQL: cache miss → compute from database
        var result = await ComputeTrendsAsync(from, to, groupBy);
        var list   = result.ToList();

        // ③ Redis: store for 5 minutes (trends are more expensive to compute)
        await cache.SetAsync(cacheKey, list, TimeSpan.FromMinutes(5));

        return list;
    }

    private async Task<IEnumerable<TrendDataPointDto>> ComputeTrendsAsync(
        DateTime from, DateTime to, string groupBy)
    {
        var data = await db.Feedbacks.AsNoTracking()
            .Where(f => f.CreatedAt >= from && f.CreatedAt <= to)
            .ToListAsync();

        IEnumerable<IGrouping<string, CustomerFeedback>> groups = groupBy.ToLower() switch
        {
            "week"  => data.GroupBy(f => $"{f.CreatedAt.Year}-W{System.Globalization.ISOWeek.GetWeekOfYear(f.CreatedAt):D2}"),
            "month" => data.GroupBy(f => f.CreatedAt.ToString("yyyy-MM")),
            _       => data.GroupBy(f => f.CreatedAt.ToString("yyyy-MM-dd"))  // day (default)
        };

        return groups
            .OrderBy(g => g.Key)
            .Select(g => new TrendDataPointDto
            {
                Date          = g.Key,
                Count         = g.Count(),
                AverageRating = g.Average(f => f.Rating),
                PositiveCount = g.Count(f => f.Sentiment == SentimentType.Positive),
                NegativeCount = g.Count(f => f.Sentiment == SentimentType.Negative)
            });
    }
}
