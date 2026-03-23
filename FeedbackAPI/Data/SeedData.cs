using FeedbackAPI.Data;
using FeedbackAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace FeedbackAPI.Data;

/// <summary>Seeds representative sample data into the in-memory database on startup.</summary>
public static class SeedData
{
    private static readonly string[] Regions = ["North", "South", "East", "West", "Central"];
    private static readonly string[] Products = ["PROD-001", "PROD-002", "PROD-003", "PROD-004", "PROD-005"];
    private static readonly string[] PositiveComments =
    [
        "Absolutely love the product, exceeded my expectations!",
        "Great customer service, very responsive and helpful.",
        "Fast delivery and product quality is excellent.",
        "Seamless experience from order to delivery.",
        "The website is intuitive and easy to navigate."
    ];
    private static readonly string[] NeutralComments =
    [
        "Product is okay, nothing special but does the job.",
        "Average customer service, got the issue resolved eventually.",
        "Pricing seems fair for what you get.",
        "Delivery was on time but packaging could be better.",
        "Website works fine but could use some improvements."
    ];
    private static readonly string[] NegativeComments =
    [
        "Product quality is disappointing, not worth the price.",
        "Customer service was unhelpful and rude.",
        "Delivery was very late and the item arrived damaged.",
        "Overpriced compared to competitors.",
        "Website kept crashing during checkout."
    ];

    public static async Task SeedAsync(FeedbackDbContext db)
    {
        if (await db.Feedbacks.AnyAsync()) return;

        var rng = new Random(42);
        var feedbacks = new List<CustomerFeedback>();
        int id = 1;

        // Generate 120 records spread across the last 90 days
        for (int i = 0; i < 120; i++)
        {
            var rating    = rng.Next(1, 6);
            var sentiment = rating >= 4 ? SentimentType.Positive
                          : rating == 3 ? SentimentType.Neutral
                          : SentimentType.Negative;

            var comment = sentiment == SentimentType.Positive
                ? PositiveComments[rng.Next(PositiveComments.Length)]
                : sentiment == SentimentType.Neutral
                    ? NeutralComments[rng.Next(NeutralComments.Length)]
                    : NegativeComments[rng.Next(NegativeComments.Length)];

            feedbacks.Add(new CustomerFeedback
            {
                Id           = id++,
                CustomerName = $"Customer {id}",
                CustomerEmail = $"customer{id}@example.com",
                Category     = (FeedbackCategory)rng.Next(0, 6),
                Sentiment    = sentiment,
                Rating       = rating,
                Comment      = comment,
                CreatedAt    = DateTime.UtcNow.AddDays(-rng.Next(0, 90)).AddHours(-rng.Next(0, 24)),
                ProductId    = Products[rng.Next(Products.Length)],
                Region       = Regions[rng.Next(Regions.Length)],
                IsResolved   = rng.Next(0, 2) == 1
            });
        }

        await db.Feedbacks.AddRangeAsync(feedbacks);
        await db.SaveChangesAsync();
    }
}
