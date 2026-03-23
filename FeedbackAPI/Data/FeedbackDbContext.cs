using FeedbackAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace FeedbackAPI.Data;

public class FeedbackDbContext(DbContextOptions<FeedbackDbContext> options) : DbContext(options)
{
    public DbSet<CustomerFeedback> Feedbacks => Set<CustomerFeedback>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerFeedback>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.Property(f => f.CustomerName).IsRequired().HasMaxLength(150);
            entity.Property(f => f.CustomerEmail).HasMaxLength(200);
            entity.Property(f => f.Comment).IsRequired().HasMaxLength(2000);
            entity.Property(f => f.ProductId).HasMaxLength(50);
            entity.Property(f => f.Region).HasMaxLength(100);
            entity.HasIndex(f => f.CreatedAt);
            entity.HasIndex(f => f.Category);
            entity.HasIndex(f => f.Sentiment);
        });
    }
}
