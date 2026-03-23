using FeedbackAPI.Data;
using FeedbackAPI.DTOs;
using FeedbackAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace FeedbackAPI.Services;

public class FeedbackService(FeedbackDbContext db) : IFeedbackService
{
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

        db.Feedbacks.Add(entity);
        await db.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<FeedbackResponseDto?> UpdateAsync(int id, FeedbackUpdateDto dto)
    {
        var entity = await db.Feedbacks.FindAsync(id);
        if (entity is null) return null;

        if (dto.Category.HasValue)  entity.Category   = dto.Category.Value;
        if (dto.IsResolved.HasValue) entity.IsResolved = dto.IsResolved.Value;
        if (dto.Comment is not null) entity.Comment   = dto.Comment.Trim();

        if (dto.Rating.HasValue)
        {
            entity.Rating    = Math.Clamp(dto.Rating.Value, 1, 5);
            entity.Sentiment = InferSentiment(entity.Rating);
        }

        await db.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await db.Feedbacks.FindAsync(id);
        if (entity is null) return false;

        db.Feedbacks.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Aggregations ─────────────────────────────────────────────────────────

    public async Task<FeedbackSummaryDto> GetSummaryAsync(FeedbackFilterParams filter)
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
