namespace FeedbackAPI.Models;

/// <summary>
/// The document shape stored and searched in Elasticsearch.
/// All text fields are analysed (tokenised, lowercased) except keyword fields.
/// Elasticsearch infers the mapping from this class on first index write.
/// </summary>
public class FeedbackSearchDocument
{
    /// <summary>Matches the SQL primary key – used as the Elasticsearch document _id.</summary>
    public int Id { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>Keyword: exact-match filtering, e.g. "ProductQuality".</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Keyword: "Positive" | "Neutral" | "Negative".</summary>
    public string Sentiment { get; set; } = string.Empty;

    public int Rating { get; set; }

    /// <summary>Analysed text field – this is what full-text search runs against.</summary>
    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string? ProductId { get; set; }

    public string? Region { get; set; }

    public bool IsResolved { get; set; }
}
