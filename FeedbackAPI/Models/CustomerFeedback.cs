namespace FeedbackAPI.Models;

public enum FeedbackCategory
{
    ProductQuality,
    CustomerService,
    Pricing,
    Delivery,
    Website,
    Other
}

public enum SentimentType
{
    Positive,
    Neutral,
    Negative
}

public class CustomerFeedback
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public FeedbackCategory Category { get; set; }
    public SentimentType Sentiment { get; set; }
    public int Rating { get; set; } // 1–5
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ProductId { get; set; }
    public string? Region { get; set; }
    public bool IsResolved { get; set; } = false;
}
