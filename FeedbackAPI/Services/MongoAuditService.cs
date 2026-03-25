using System.Text.Json;
using FeedbackAPI.DTOs;
using FeedbackAPI.Models;
using MongoDB.Driver;

namespace FeedbackAPI.Services;

/// <summary>
/// Records an immutable audit trail for every feedback mutation.
/// Each change is a separate MongoDB document in the "feedback_audit" collection.
///
/// Why MongoDB for audit logs?
///   - Schema-free: "before" and "after" snapshots can evolve without migrations
///   - Append-only workload suits document stores naturally
///   - ObjectId _id encodes creation time, giving free time-ordered reads
///   - Flexible for adding JSON fields without ALTER TABLE
/// </summary>
public interface IAuditService
{
    Task LogCreatedAsync(int feedbackId, object snapshot);
    Task LogUpdatedAsync(int feedbackId, object before, object after, string? summary = null);
    Task LogDeletedAsync(int feedbackId, object snapshot);

    /// <summary>Returns all audit events for a specific feedback record, newest first.</summary>
    Task<IEnumerable<AuditLogDto>> GetLogsAsync(int feedbackId);

    /// <summary>Returns the most recent audit events across all records.</summary>
    Task<IEnumerable<AuditLogDto>> GetRecentLogsAsync(int limit = 50);
}

/// <summary>
/// MongoDB-backed audit service.
/// Uses <see cref="IMongoDatabase"/> injected via DI (registered as Scoped in Program.cs).
/// All writes are fire-and-forget-safe: a MongoDB outage never blocks the main API.
/// </summary>
public class MongoAuditService(IMongoDatabase mongoDb, ILogger<MongoAuditService> logger)
    : IAuditService
{
    // Collection name in MongoDB – will be created automatically on first write
    private const string CollectionName = "feedback_audit";

    private IMongoCollection<FeedbackAuditDocument> Collection =>
        mongoDb.GetCollection<FeedbackAuditDocument>(CollectionName);

    // ── Write helpers ────────────────────────────────────────────────────────

    public Task LogCreatedAsync(int feedbackId, object snapshot) =>
        SafeInsertAsync("Create", feedbackId,
            before: null,
            after:  snapshot,
            summary: $"New feedback #{feedbackId} created");

    public Task LogUpdatedAsync(int feedbackId, object before, object after, string? summary = null) =>
        SafeInsertAsync("Update", feedbackId,
            before: before,
            after:  after,
            summary: summary ?? $"Feedback #{feedbackId} updated");

    public Task LogDeletedAsync(int feedbackId, object snapshot) =>
        SafeInsertAsync("Delete", feedbackId,
            before: snapshot,
            after:  null,
            summary: $"Feedback #{feedbackId} deleted");

    private async Task SafeInsertAsync(
        string action, int feedbackId,
        object? before, object? after,
        string? summary)
    {
        try
        {
            var doc = new FeedbackAuditDocument
            {
                Action        = action,
                FeedbackId    = feedbackId,
                Timestamp     = DateTime.UtcNow,
                BeforeJson    = before is null ? null : JsonSerializer.Serialize(before),
                AfterJson     = after  is null ? null : JsonSerializer.Serialize(after),
                ChangeSummary = summary
            };
            // InsertOneAsync is a non-blocking network call
            await Collection.InsertOneAsync(doc);
            logger.LogDebug("MongoDB audit {Action} FeedbackId={Id}", action, feedbackId);
        }
        catch (Exception ex)
        {
            // A MongoDB outage must NEVER fail the user's CRUD request
            logger.LogWarning(ex,
                "MongoDB audit write failed for {Action} on FeedbackId={Id}. Main operation succeeded.",
                action, feedbackId);
        }
    }

    // ── Read helpers ─────────────────────────────────────────────────────────

    public async Task<IEnumerable<AuditLogDto>> GetLogsAsync(int feedbackId)
    {
        try
        {
            var filter = Builders<FeedbackAuditDocument>.Filter.Eq(d => d.FeedbackId, feedbackId);
            var sort   = Builders<FeedbackAuditDocument>.Sort.Descending(d => d.Timestamp);
            var docs   = await Collection.Find(filter).Sort(sort).ToListAsync();
            return docs.Select(ToDto);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MongoDB GetLogsAsync failed for FeedbackId={Id}", feedbackId);
            return [];
        }
    }

    public async Task<IEnumerable<AuditLogDto>> GetRecentLogsAsync(int limit = 50)
    {
        try
        {
            var sort = Builders<FeedbackAuditDocument>.Sort.Descending(d => d.Timestamp);
            var docs = await Collection
                .Find(FilterDefinition<FeedbackAuditDocument>.Empty)
                .Sort(sort)
                .Limit(Math.Min(limit, 200))   // safety cap
                .ToListAsync();
            return docs.Select(ToDto);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MongoDB GetRecentLogsAsync failed");
            return [];
        }
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private static AuditLogDto ToDto(FeedbackAuditDocument doc) => new()
    {
        Id            = doc.Id,
        Action        = doc.Action,
        FeedbackId    = doc.FeedbackId,
        Timestamp     = doc.Timestamp,
        ChangeSummary = doc.ChangeSummary,
        BeforeJson    = doc.BeforeJson,
        AfterJson     = doc.AfterJson
    };
}
