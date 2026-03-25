using FeedbackAPI.Models;

namespace FeedbackAPI.DTOs;

/// <summary>Payload to create a new feedback entry.</summary>
public class FeedbackCreateDto
{
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public FeedbackCategory Category { get; set; }
    public int Rating { get; set; } // 1–5
    public string Comment { get; set; } = string.Empty;
    public string? ProductId { get; set; }
    public string? Region { get; set; }
}

/// <summary>Payload to update an existing feedback entry.</summary>
public class FeedbackUpdateDto
{
    public FeedbackCategory? Category { get; set; }
    public int? Rating { get; set; }
    public string? Comment { get; set; }
    public bool? IsResolved { get; set; }
}

/// <summary>Full feedback record returned to clients.</summary>
public class FeedbackResponseDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Sentiment { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? ProductId { get; set; }
    public string? Region { get; set; }
    public bool IsResolved { get; set; }
}

/// <summary>Aggregated summary statistics.</summary>
public class FeedbackSummaryDto
{
    public int TotalCount { get; set; }
    public double AverageRating { get; set; }
    public int PositiveCount { get; set; }
    public int NeutralCount { get; set; }
    public int NegativeCount { get; set; }
    public int ResolvedCount { get; set; }
    public Dictionary<string, int> CountByCategory { get; set; } = new();
    public Dictionary<string, int> CountByRegion { get; set; } = new();
    public Dictionary<string, double> AvgRatingByCategory { get; set; } = new();
}

/// <summary>A single data point on a time-series trend chart.</summary>
public class TrendDataPointDto
{
    public string Date { get; set; } = string.Empty; // ISO date string
    public int Count { get; set; }
    public double AverageRating { get; set; }
    public int PositiveCount { get; set; }
    public int NegativeCount { get; set; }
}

/// <summary>Query parameters for filtering feedback.</summary>
public class FeedbackFilterParams
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public FeedbackCategory? Category { get; set; }
    public SentimentType? Sentiment { get; set; }
    public int? MinRating { get; set; }
    public int? MaxRating { get; set; }
    public string? Region { get; set; }
    public string? ProductId { get; set; }
    public bool? IsResolved { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>Paginated list wrapper.</summary>
public class PagedResult<T>
{
    public IEnumerable<T> Data { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

// ── Elasticsearch DTOs ────────────────────────────────────────────────────────

/// <summary>A single Elasticsearch hit: the feedback item + relevance score + highlighted fragment.</summary>
public class SearchHit
{
    public FeedbackResponseDto Item { get; set; } = null!;
    /// <summary>Relevance score from Elasticsearch (higher = more relevant).</summary>
    public double Score { get; set; }
    /// <summary>HTML fragment with the matching text wrapped in &lt;mark&gt; tags. Null when no highlight.</summary>
    public string? Highlight { get; set; }
}

/// <summary>Full-text search result page returned by Elasticsearch.</summary>
public class SearchResultDto
{
    public string Query { get; set; } = string.Empty;
    public long TotalHits { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IEnumerable<SearchHit> Hits { get; set; } = [];
}

// ── MongoDB DTOs ────────────────────────────────────────────────────────────

/// <summary>An audit log entry read from MongoDB.</summary>
public class AuditLogDto
{
    /// <summary>MongoDB ObjectId as string – time-encoded (first 8 hex chars = Unix timestamp).</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>"Create" | "Update" | "Delete"</summary>
    public string Action { get; set; } = string.Empty;
    public int FeedbackId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? ChangeSummary { get; set; }
    /// <summary>JSON snapshot of the record before the change (null for Create).</summary>
    public string? BeforeJson { get; set; }
    /// <summary>JSON snapshot of the record after the change (null for Delete).</summary>
    public string? AfterJson { get; set; }
}
