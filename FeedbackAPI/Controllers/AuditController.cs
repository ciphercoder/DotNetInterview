using FeedbackAPI.DTOs;
using FeedbackAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace FeedbackAPI.Controllers;

/// <summary>
/// Read-only access to the MongoDB audit trail.
/// Endpoints:
///   GET /api/audit/feedback/{id}  – all changes to a specific feedback record
///   GET /api/audit/recent         – most recent events across all records
///
/// Why separate from FeedbackController?
///   The audit log is a cross-cutting concern stored in a different database (MongoDB).
///   Keeping it isolated means no SQL/EF code touches this controller at all.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuditController(IAuditService audit) : ControllerBase
{
    /// <summary>Returns the full change history for a specific feedback record (MongoDB).</summary>
    /// <param name="id">SQL primary key of the feedback record.</param>
    [HttpGet("feedback/{id:int}")]
    [ProducesResponseType(typeof(IEnumerable<AuditLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForFeedback(int id)
    {
        var logs = await audit.GetLogsAsync(id);
        return Ok(logs);
    }

    /// <summary>Returns the most recent audit events across all feedback records.</summary>
    /// <param name="limit">Maximum number of events to return (max 200).</param>
    [HttpGet("recent")]
    [ProducesResponseType(typeof(IEnumerable<AuditLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecent([FromQuery] int limit = 50)
    {
        var logs = await audit.GetRecentLogsAsync(Math.Min(limit, 200));
        return Ok(logs);
    }
}
