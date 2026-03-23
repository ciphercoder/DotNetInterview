using FeedbackAPI.DTOs;
using FeedbackAPI.Models;
using FeedbackAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace FeedbackAPI.Controllers;

/// <summary>CRUD + aggregation endpoints for customer feedback.</summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class FeedbackController(IFeedbackService service) : ControllerBase
{
    // ── GET /api/feedback ─────────────────────────────────────────────────────
    /// <summary>Returns a paginated, filtered list of feedback entries.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<FeedbackResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] FeedbackFilterParams filter)
    {
        var result = await service.GetAllAsync(filter);
        return Ok(result);
    }

    // ── GET /api/feedback/{id} ────────────────────────────────────────────────
    /// <summary>Returns a single feedback entry by ID.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(FeedbackResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await service.GetByIdAsync(id);
        return item is null ? NotFound() : Ok(item);
    }

    // ── POST /api/feedback ────────────────────────────────────────────────────
    /// <summary>Creates a new feedback entry. Sentiment is auto-inferred from the rating.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(FeedbackResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] FeedbackCreateDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var created = await service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    // ── PUT /api/feedback/{id} ────────────────────────────────────────────────
    /// <summary>Partially updates category, rating, comment, or resolved status.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(FeedbackResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] FeedbackUpdateDto dto)
    {
        var updated = await service.UpdateAsync(id, dto);
        return updated is null ? NotFound() : Ok(updated);
    }

    // ── DELETE /api/feedback/{id} ─────────────────────────────────────────────
    /// <summary>Permanently deletes a feedback entry.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await service.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }

    // ── GET /api/feedback/summary ─────────────────────────────────────────────
    /// <summary>
    /// Returns aggregated statistics: total count, average rating, sentiment breakdown,
    /// resolved count, counts by category and region, average rating per category.
    /// Supports the same filter parameters as the list endpoint.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(FeedbackSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary([FromQuery] FeedbackFilterParams filter)
    {
        var summary = await service.GetSummaryAsync(filter);
        return Ok(summary);
    }

    // ── GET /api/feedback/trends ──────────────────────────────────────────────
    /// <summary>
    /// Returns time-series trend data grouped by day (default), week, or month.
    /// Each point contains the count, average rating, and positive/negative breakdown.
    /// </summary>
    [HttpGet("trends")]
    [ProducesResponseType(typeof(IEnumerable<TrendDataPointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTrends(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string groupBy = "day")
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end   = to   ?? DateTime.UtcNow;

        if (start > end)
            return BadRequest("'from' must be before 'to'.");

        var allowed = new[] { "day", "week", "month" };
        if (!allowed.Contains(groupBy.ToLower()))
            return BadRequest($"'groupBy' must be one of: {string.Join(", ", allowed)}.");

        var trends = await service.GetTrendsAsync(start, end, groupBy);
        return Ok(trends);
    }

    // ── GET /api/feedback/categories ─────────────────────────────────────────
    /// <summary>Returns all available feedback categories as strings.</summary>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
    public IActionResult GetCategories()
        => Ok(Enum.GetNames<FeedbackCategory>());

    // ── GET /api/feedback/regions ─────────────────────────────────────────────
    /// <summary>Returns the distinct regions present in the feedback data.</summary>
    [HttpGet("regions")]
    [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRegions()
    {
        var result = await service.GetAllAsync(new FeedbackFilterParams { PageSize = int.MaxValue });
        var regions = result.Data
            .Where(f => !string.IsNullOrWhiteSpace(f.Region))
            .Select(f => f.Region!)
            .Distinct()
            .OrderBy(r => r);
        return Ok(regions);
    }
}
