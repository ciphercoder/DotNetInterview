using FeedbackAPI.DTOs;
using FeedbackAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace FeedbackAPI.Controllers;

/// <summary>
/// Full-text search over feedback records via Elasticsearch.
/// Endpoint: GET /api/search?q=broken+delivery&amp;page=1&amp;pageSize=20
///
/// Why a separate controller?
///   Search is a different read path from CRUD. It talks to Elasticsearch,
///   not SQL. Keeping it separate honours Single Responsibility.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SearchController(ISearchService search) : ControllerBase
{
    /// <summary>
    /// Full-text search on feedback comments, customer names, categories and regions.
    /// Supports fuzzy matching (typo tolerance) and returns highlighted snippets.
    /// </summary>
    /// <param name="q">The search query string.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Results per page (max 100).</param>
    [HttpGet]
    [ProducesResponseType(typeof(SearchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] string q,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 20)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Query parameter 'q' is required.");

        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        var result = await search.SearchAsync(q, page, pageSize);
        return Ok(result);
    }
}
