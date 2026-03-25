using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using FeedbackAPI.DTOs;
using FeedbackAPI.Models;

namespace FeedbackAPI.Services;

/// <summary>
/// Full-text search over feedback comments and metadata using Elasticsearch.
///
/// Why Elasticsearch for search?
///   - SQL LIKE '%term%' can't use indexes → full table scan on every search
///   - ES inverted index: sub-millisecond search on millions of documents
///   - Built-in fuzzy matching (handles typos: "prblm" → "problem")
///   - Hit highlighting: returns the matching sentence with &lt;mark&gt; tags
///   - Relevance scoring: best match surfaces first, not just row order
/// </summary>
public interface ISearchService
{
    /// <summary>Indexes a single feedback document (called after Create / Update).</summary>
    Task IndexAsync(FeedbackResponseDto feedback);

    /// <summary>Removes a document from the index (called after Delete).</summary>
    Task DeleteAsync(int feedbackId);

    /// <summary>
    /// Full-text search on comment, customerName, category, region.
    /// Supports fuzzy matching and returns highlighted fragments.
    /// </summary>
    Task<SearchResultDto> SearchAsync(string query, int page = 1, int pageSize = 20);

    /// <summary>Bulk-indexes multiple items (used for initial ES seeding from SQL).</summary>
    Task BulkIndexAsync(IEnumerable<FeedbackResponseDto> items);
}

/// <summary>
/// Elasticsearch 8.x implementation using the official Elastic.Clients.Elasticsearch client.
///
/// Index name: "feedback"
/// ES infers the index mapping from <see cref="FeedbackSearchDocument"/> on first write.
/// To use a custom mapping, call client.Indices.CreateAsync before first Index call.
/// </summary>
public class ElasticsearchService(
    ElasticsearchClient client,
    ILogger<ElasticsearchService> logger) : ISearchService
{
    private const string IndexName = "feedback";

    // ── Index (upsert) ───────────────────────────────────────────────────────

    public async Task IndexAsync(FeedbackResponseDto feedback)
    {
        try
        {
            var doc = ToSearchDocument(feedback);

            // Id(doc.Id.ToString()) makes Elasticsearch use the SQL PK as the ES document _id.
            // This means re-indexing (after an Update) is an upsert – no duplicates.
            var response = await client.IndexAsync(doc,
                i => i.Index(IndexName).Id(doc.Id.ToString()));

            if (!response.IsValidResponse)
                logger.LogWarning("ES index failed for Id={Id}: {Info}",
                    feedback.Id, response.DebugInformation);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ES IndexAsync failed for Id={Id}", feedback.Id);
        }
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    public async Task DeleteAsync(int feedbackId)
    {
        try
        {
            var response = await client.DeleteAsync<FeedbackSearchDocument>(
                feedbackId.ToString(), d => d.Index(IndexName));

            if (!response.IsValidResponse && response.Result != Result.NotFound)
                logger.LogWarning("ES delete failed for Id={Id}: {Info}",
                    feedbackId, response.DebugInformation);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ES DeleteAsync failed for Id={Id}", feedbackId);
        }
    }

    // ── Bulk index ───────────────────────────────────────────────────────────

    public async Task BulkIndexAsync(IEnumerable<FeedbackResponseDto> items)
    {
        var docs = items.Select(ToSearchDocument).ToList();
        if (docs.Count == 0) return;

        try
        {
            // BulkAsync sends all documents in a single HTTP request → far more efficient than
            // N individual IndexAsync calls for seeding / re-indexing.
            var response = await client.BulkAsync(b => b
                .Index(IndexName)
                .IndexMany(docs, (op, doc) => op.Id(doc.Id.ToString())));

            if (response.Errors)
                logger.LogWarning("ES bulk index had {Count} errors",
                    response.ItemsWithErrors.Count());
            else
                logger.LogInformation("ES bulk indexed {Count} documents", docs.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ES BulkIndexAsync failed ({Count} docs)", docs.Count);
        }
    }

    // ── Search ───────────────────────────────────────────────────────────────

    public async Task<SearchResultDto> SearchAsync(string query, int page = 1, int pageSize = 20)
    {
        try
        {
            int from = (page - 1) * pageSize;

            var response = await client.SearchAsync<FeedbackSearchDocument>(s => s
                .Indices(IndexName)
                .From(from)
                .Size(pageSize)
                .Query(q => q
                    // Bool/Should is equivalent to MultiMatch across multiple fields.
                    // Should = OR logic: matches ANY field. Boost(2.0) on comment means
                    // hits in the comment field rank higher than hits in other fields.
                    .Bool(b => b
                        .Should(
                            // comment is the primary search field – boost 2x for relevance
                            s => s.Match(m => m
                                .Field(fd => fd.Comment)
                                .Query(query)
                                .Fuzziness(new Fuzziness("AUTO"))
                                .Boost(2.0f)),
                            s => s.Match(m => m
                                .Field(fd => fd.CustomerName)
                                .Query(query)
                                .Fuzziness(new Fuzziness("AUTO"))),
                            s => s.Match(m => m
                                .Field(fd => fd.Category)
                                .Query(query)),
                            s => s.Match(m => m
                                .Field(fd => fd.Region)
                                .Query(query))
                        )
                        .MinimumShouldMatch(1)
                    )
                )
                .Highlight(h => h
                    // Pre/PostTags wrap the matching fragment in HTML so the UI can highlight it
                    .PreTags("<mark>")
                    .PostTags("</mark>")
                    .Fields(hf => hf
                        .Add(new Field("comment"),
                            new HighlightField { NumberOfFragments = 3 })
                    )
                )
            );

            if (!response.IsValidResponse)
            {
                logger.LogWarning("ES search failed for '{Query}': {Info}", query, response.DebugInformation);
                return EmptyResult(query, page, pageSize);
            }

            var hits = response.Hits.Select(h =>
            {
                // Extract the highlighted comment snippet (if any)
                string? highlight = null;
                if (h.Highlight != null &&
                    h.Highlight.TryGetValue("comment", out var fragments) &&
                    fragments.Count > 0)
                {
                    highlight = string.Join(" … ", fragments);
                }

                return new SearchHit
                {
                    Item      = MapToDto(h.Source!),
                    Score     = h.Score ?? 0,
                    Highlight = highlight
                };
            }).ToList();

            return new SearchResultDto
            {
                Query     = query,
                TotalHits = response.HitsMetadata?.Total?.Match(th => th?.Value ?? 0L, l => l) ?? 0L,
                Page      = page,
                PageSize  = pageSize,
                Hits      = hits
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ES SearchAsync exception for '{Query}'", query);
            return EmptyResult(query, page, pageSize);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static FeedbackSearchDocument ToSearchDocument(FeedbackResponseDto f) => new()
    {
        Id           = f.Id,
        CustomerName = f.CustomerName,
        CustomerEmail = f.CustomerEmail ?? string.Empty,
        Category     = f.Category,
        Sentiment    = f.Sentiment,
        Rating       = f.Rating,
        Comment      = f.Comment,
        CreatedAt    = f.CreatedAt,
        ProductId    = f.ProductId,
        Region       = f.Region,
        IsResolved   = f.IsResolved
    };

    private static FeedbackResponseDto MapToDto(FeedbackSearchDocument s) => new()
    {
        Id            = s.Id,
        CustomerName  = s.CustomerName,
        CustomerEmail = s.CustomerEmail,
        Category      = s.Category,
        Sentiment     = s.Sentiment,
        Rating        = s.Rating,
        Comment       = s.Comment,
        CreatedAt     = s.CreatedAt,
        ProductId     = s.ProductId,
        Region        = s.Region,
        IsResolved    = s.IsResolved
    };

    private static SearchResultDto EmptyResult(string query, int page, int pageSize) => new()
    {
        Query    = query,
        Page     = page,
        PageSize = pageSize,
        Hits     = []
    };
}
