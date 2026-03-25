using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FeedbackAPI.Models;

/// <summary>
/// MongoDB document that records every create / update / delete event on a feedback record.
/// Stored in the "feedback_audit" collection. Each document is immutable once written.
/// </summary>
public class FeedbackAuditDocument
{
    /// <summary>MongoDB primary key – ObjectId is time-ordered so sorting by _id == sorting by time.</summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>"Create" | "Update" | "Delete"</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The SQL primary key of the feedback record this event is about.</summary>
    public int FeedbackId { get; set; }

    /// <summary>UTC timestamp when the event occurred.</summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// JSON snapshot of the record BEFORE the change.
    /// Null for "Create" events (there was no prior state).
    /// </summary>
    public string? BeforeJson { get; set; }

    /// <summary>
    /// JSON snapshot of the record AFTER the change.
    /// Null for "Delete" events (there is no new state).
    /// </summary>
    public string? AfterJson { get; set; }

    /// <summary>Human-readable one-liner describing the change (optional).</summary>
    public string? ChangeSummary { get; set; }
}
