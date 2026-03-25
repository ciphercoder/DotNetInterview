# Polyglot Persistence Reference
### SQL + Redis + MongoDB + Elasticsearch in one .NET 8 Web API

---

## TABLE OF CONTENTS

1. [What is Polyglot Persistence?](#1-what-is-polyglot-persistence)
2. [Architecture – Four Stores, One API](#2-architecture--four-stores-one-api)
3. [How the Stores Are Wired Together](#3-how-the-stores-are-wired-together)
4. [SQL / EF Core – The Primary Store](#4-sql--ef-core--the-primary-store)
5. [Redis – The Cache Layer](#5-redis--the-cache-layer)
6. [MongoDB – The Audit Log](#6-mongodb--the-audit-log)
7. [Elasticsearch – Full-Text Search](#7-elasticsearch--full-text-search)
8. [New Endpoints Added](#8-new-endpoints-added)
9. [Running All Four Services Locally](#9-running-all-four-services-locally)
10. [Design Decisions Explained](#10-design-decisions-explained)
11. [Interview Q&A](#11-interview-qa)

---

## 1. What is Polyglot Persistence?

**Polyglot persistence** = using different databases for different parts of your problem, choosing the best tool for each job.

The analogy: you don't use a screwdriver to hammer a nail. Each data store has a *superpower*:

| Database | Superpower | Weakness |
|---|---|---|
| SQL (EF Core / SQL Server) | Structured data, JOINs, ACID transactions, aggregations | Full-text search, schema changes |
| Redis | Sub-millisecond reads, cache, pub/sub | Not a primary store, data can evict |
| MongoDB | Flexible schema (no ALTER TABLE), append-only logs, rich documents | Complex JOINs, transactions |
| Elasticsearch | Full-text search, fuzzy matching, relevance scoring, highlights | Not a primary store, eventual consistency |

In this project:
- Customer feedback **is stored once** (SQL) – single source of truth
- The dashboard KPIs **are cached** (Redis) – aggregations are expensive
- Every mutation **is tracked** (MongoDB) – immutable audit trail
- Feedback comments **are indexed** (Elasticsearch) – fast free-text search

---

## 2. Architecture – Four Stores, One API

```
Browser / Swagger
       │
       ▼
  .NET 8 Web API (port 5000)
       │
       ├─── FeedbackController ──────────────── SQL (EF Core / InMemory)
       │         (CRUD + summary + trends)          Primary store
       │                  │
       │                  ├── on GET summary/trends → Redis (cache-aside)
       │                  ├── on POST/PUT/DELETE   → Elasticsearch (index/delete)
       │                  └── on POST/PUT/DELETE   → MongoDB (audit log)
       │
       ├─── SearchController ──────────────────── Elasticsearch
       │         (GET /api/search?q=...)             Full-text search
       │
       └─── AuditController ──────────────────── MongoDB
                 (GET /api/audit/...)                Change history
```

**Read path (GET /api/feedback/summary):**
```
1. RedisCacheService.GetAsync(key)    → HIT  → return cached DTO immediately   (< 1ms)
                                      → MISS → continue to step 2
2. FeedbackDbContext SQL query         → expensive GROUP BY in-memory          (~ 10ms)
3. RedisCacheService.SetAsync(key)    → cache result for 60 seconds
4. return result
```

**Write path (POST /api/feedback):**
```
1. SQL INSERT (EF Core) – must succeed
2. In parallel (Task.WhenAll):
   a. ES   IndexAsync   – index comment for full-text search
   b. MongoDB LogCreatedAsync – record audit event
   c. Redis RemoveAsync – invalidate summary cache
3. Return 201 Created
```

If steps 2a/2b/2c fail, the HTTP 201 is **still returned** – audit/search failures are never surfaced to callers.

---

## 3. How the Stores Are Wired Together

### 3.1 Program.cs bootstrap (the complete picture)

```csharp
var builder = WebApplication.CreateBuilder(args);

// ── SQL (EF Core) ──────────────────────────────────────────────────────────
builder.Services.AddDbContext<FeedbackDbContext>(opt =>
    opt.UseInMemoryDatabase("FeedbackDb"));   // swap to UseSqlServer() in prod
builder.Services.AddScoped<IFeedbackService, FeedbackService>();

// ── Redis ──────────────────────────────────────────────────────────────────
// AddStackExchangeRedisCache registers IDistributedCache backed by Redis.
// InstanceName is a key prefix: "FeedbackAPI:feedback:summary:..."
builder.Services.AddStackExchangeRedisCache(opt =>
{
    opt.Configuration = builder.Configuration.GetConnectionString("Redis")
                        ?? "localhost:6379";
    opt.InstanceName  = "FeedbackAPI:";
});
builder.Services.AddScoped<ICacheService, RedisCacheService>();

// ── MongoDB ────────────────────────────────────────────────────────────────
// IMongoClient: Singleton – one connection pool per process (heavy object)
// IMongoDatabase: Scoped – lightweight reference, create per request
var mongoConnStr = builder.Configuration.GetConnectionString("MongoDB")
                   ?? "mongodb://localhost:27017";
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnStr));
builder.Services.AddScoped<IMongoDatabase>(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase("FeedbackDB"));
builder.Services.AddScoped<IAuditService, MongoAuditService>();

// ── Elasticsearch ──────────────────────────────────────────────────────────
// ElasticsearchClient: Singleton – thread-safe, manages its own HTTP pool
var esUrl      = builder.Configuration.GetConnectionString("Elasticsearch")
                 ?? "http://localhost:9200";
var esSettings = new ElasticsearchClientSettings(new Uri(esUrl))
    .DefaultIndex("feedback");
builder.Services.AddSingleton(new ElasticsearchClient(esSettings));
builder.Services.AddScoped<ISearchService, ElasticsearchService>();
```

**DI lifetime rules for the four stores:**

| Store | .NET Type | Lifetime | Why |
|---|---|---|---|
| SQL (EF Core) | `FeedbackDbContext` | **Scoped** | One DbContext per HTTP request (unit-of-work pattern) |
| Redis | `IDistributedCache` | **Scoped** | ASP.NET Core registers it as scoped by default |
| MongoDB client | `IMongoClient` | **Singleton** | Expensive to create; connection pool must be shared |
| MongoDB database | `IMongoDatabase` | **Scoped** | Lightweight; safe to create per request |
| Elasticsearch | `ElasticsearchClient` | **Singleton** | Thread-safe; manages its own HTTP pool |

### 3.2 FeedbackService orchestration

The `FeedbackService` primary constructor takes **all four** store dependencies:

```csharp
public class FeedbackService(
    FeedbackDbContext db,      // SQL
    ICacheService     cache,   // Redis
    IAuditService     audit,   // MongoDB
    ISearchService    search)  // Elasticsearch
    : IFeedbackService
{
    // ① Create: SQL → ES index → MongoDB audit → Redis cache bust
    public async Task<FeedbackResponseDto> CreateAsync(FeedbackCreateDto dto)
    {
        // Primary write – if this fails, nothing else runs
        db.Feedbacks.Add(entity);
        await db.SaveChangesAsync();

        // Secondary writes run in parallel; any failure is caught inside each service
        await Task.WhenAll(
            search.IndexAsync(result),           // ES: document for search
            audit.LogCreatedAsync(id, result),   // MongoDB: audit event
            cache.RemoveAsync(SummaryCacheKey())  // Redis: invalidate cache
        );
        return result;
    }

    // ② Read summary: Redis first, SQL on miss
    public async Task<FeedbackSummaryDto> GetSummaryAsync(FeedbackFilterParams filter)
    {
        var cacheKey = SummaryCacheKey(filter);

        var cached = await cache.GetAsync<FeedbackSummaryDto>(cacheKey);
        if (cached is not null) return cached;           // ← Redis HIT

        // Cache MISS: compute from SQL
        var result = await ComputeSummaryAsync(filter);

        await cache.SetAsync(cacheKey, result, TimeSpan.FromSeconds(60));
        return result;
    }
}
```

---

## 4. SQL / EF Core – The Primary Store

### Role
Every feedback record lives here first and last. It is the **source of truth**. If Redis, MongoDB, or Elasticsearch go down, the API continues working.

### What makes it the right choice for feedback CRUD?
- **Structured data**: feedback has a fixed schema (14 columns)
- **ACID transactions**: create + update history must not partially complete
- **Aggregations**: `COUNT`, `GROUP BY`, `AVG` are SQL's native operations
- **Relationships**: future foreign keys (users, products) fit naturally

### Key EF Core patterns already in the code

```csharp
// Read-only query: AsNoTracking() skips the EF change tracker overhead
var query = db.Feedbacks.AsNoTracking().Where(f => f.Category == theCategory);

// Dynamic filter building – IQueryable is lazy, no SQL runs yet
if (filter.From.HasValue) query = query.Where(f => f.CreatedAt >= filter.From.Value);
if (filter.To.HasValue)   query = query.Where(f => f.CreatedAt <= filter.To.Value);

// Run the SQL – two queries for pagination
var count = await query.CountAsync();                      // SELECT COUNT(*)
var page  = await query.Skip(skip).Take(pageSize)          // SELECT ... LIMIT / OFFSET
                        .ToListAsync();

// Update: FindAsync tracks the entity, so EF knows what changed
var entity = await db.Feedbacks.FindAsync(id);  // if cached in tracker, no DB round-trip
entity.Rating = 4;
await db.SaveChangesAsync();                     // EF generates UPDATE only for changed fields
```

### Production swap
Change one line in `Program.cs`:
```csharp
// Dev:  opt.UseInMemoryDatabase("FeedbackDb")
// Prod: opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
// Prod: opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
```
Then run `dotnet ef migrations add InitialCreate` and `dotnet ef database update`.

---

## 5. Redis – The Cache Layer

### What Redis is
Redis is an in-memory key-value store. Reading from Redis is **5,000× faster** than even a well-indexed SQL query because:
- Data lives in RAM (no disk I/O)
- No query planning
- No row deserialization

### How we use it: Cache-Aside pattern

```
Application → check Redis key
  HIT  → return data immediately (done!)
  MISS → query SQL → store in Redis with TTL → return data
```

On mutation (create/update/delete):
```
→ SQL write succeeds
→ delete relevant Redis keys
→ next request gets fresh data from SQL (which then re-caches it)
```

### Complete CacheService implementation

```csharp
// ICacheService – our abstraction (can swap Redis for InMemory cache in tests)
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class;
    Task RemoveAsync(string key);
}

// RedisCacheService – backed by IDistributedCache (registered in Program.cs)
public class RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger)
    : ICacheService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var json = await cache.GetStringAsync(key);
            return json is null ? null : JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            // ← Graceful degradation: Redis down → fall through to SQL, no crash
            logger.LogWarning(ex, "Redis GET failed for '{Key}'", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class
    {
        try
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl
            };
            await cache.SetStringAsync(key, JsonSerializer.Serialize(value), options);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis SET failed for '{Key}'", key);
        }
    }
}
```

### Cache key design
Keys are deterministic strings built from the query parameters:
```csharp
private static string SummaryCacheKey(FeedbackFilterParams f) =>
    $"feedback:summary:{f.Category}:{f.Sentiment}:{f.MinRating}:{f.MaxRating}" +
    $":{f.From:yyyyMMdd}:{f.To:yyyyMMdd}:{f.Region}:{f.ProductId}:{f.IsResolved}";
// e.g. "feedback:summary::Negative:3:5:20240101:20240131:UK:"
```

**Why colon-delimited?** Redis uses `:` as a conventional namespace separator. Redis GUI tools (RedisInsight) group keys by namespace automatically.

### TTLs in this project

| Cache | TTL | Why |
|---|---|---|
| Summary (KPIs) | 60 seconds | Dashboard refresh acceptable at 1-minute staleness |
| Trends | 5 minutes | More expensive query; trends don't change second-by-second |

### Program.cs registration
```csharp
builder.Services.AddStackExchangeRedisCache(opt =>
{
    opt.Configuration = "localhost:6379";     // or "redis:6379" in Docker
    opt.InstanceName  = "FeedbackAPI:";       // all keys prefixed automatically
});
builder.Services.AddScoped<ICacheService, RedisCacheService>();
```

### Why IDistributedCache instead of raw StackExchange.Redis?
`IDistributedCache` is a .NET abstraction. In tests you can register `AddDistributedMemoryCache()` instead of Redis – your service code changes zero lines.

### Redis connection string formats
```
localhost:6379                        # local
redis:6379                            # Docker service name
redis:6379,password=secret            # with auth
redis:6379,abortConnect=false,ssl=true # Azure Cache for Redis
```

---

## 6. MongoDB – The Audit Log

### What MongoDB is
MongoDB is a **document database**. Instead of rows in tables, it stores JSON-like documents (BSON) in **collections**. Each document can have different fields – no fixed schema required.

### Why MongoDB for audit logs?
- **Append-only**: audit records are never updated, only inserted → documents are perfect
- **Flexible schema**: the "before" and "after" snapshots evolve as the entity schema changes
- **ObjectId _id**: MongoDB's built-in ID is time-ordered; sorting by `_id` = sorting by creation time
- **No schema migration**: adding a new field to the entity never requires `ALTER TABLE`

### Document structure

```csharp
// Models/FeedbackAuditDocument.cs
public class FeedbackAuditDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    // ^ ObjectId = 12 bytes: 4 bytes timestamp + 5 bytes random + 3 bytes counter
    // The first 4 bytes encode the Unix timestamp → documents are naturally time-sorted

    public string Action { get; set; } = string.Empty;   // "Create" | "Update" | "Delete"
    public int FeedbackId { get; set; }                   // SQL foreign key reference

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string? BeforeJson { get; set; }   // null for Create events
    public string? AfterJson  { get; set; }   // null for Delete events
    public string? ChangeSummary { get; set; }
}
```

In MongoDB this looks like:
```json
{
  "_id": ObjectId("65f3a1b2c3d4e5f6a7b8c9d0"),
  "Action": "Update",
  "FeedbackId": 42,
  "Timestamp": ISODate("2024-03-14T10:22:33Z"),
  "BeforeJson": "{\"rating\":2,\"comment\":\"Terrible product\"}",
  "AfterJson":  "{\"rating\":4,\"comment\":\"Much better after replacement\",\"isResolved\":true}",
  "ChangeSummary": "Feedback #42 updated"
}
```

### Complete MongoAuditService

```csharp
// IAuditService
public interface IAuditService
{
    Task LogCreatedAsync(int feedbackId, object snapshot);
    Task LogUpdatedAsync(int feedbackId, object before, object after, string? summary = null);
    Task LogDeletedAsync(int feedbackId, object snapshot);
    Task<IEnumerable<AuditLogDto>> GetLogsAsync(int feedbackId);
    Task<IEnumerable<AuditLogDto>> GetRecentLogsAsync(int limit = 50);
}

// Implementation key patterns
public class MongoAuditService(IMongoDatabase mongoDb, ILogger<MongoAuditService> logger)
    : IAuditService
{
    private IMongoCollection<FeedbackAuditDocument> Collection =>
        mongoDb.GetCollection<FeedbackAuditDocument>("feedback_audit");
    // ^ Collection is lazily resolved on each access; the collection is auto-created on first write

    private async Task SafeInsertAsync(string action, int feedbackId,
        object? before, object? after, string? summary)
    {
        try
        {
            var doc = new FeedbackAuditDocument
            {
                Action        = action,
                FeedbackId    = feedbackId,
                BeforeJson    = before is null ? null : JsonSerializer.Serialize(before),
                AfterJson     = after  is null ? null : JsonSerializer.Serialize(after),
                ChangeSummary = summary
            };
            await Collection.InsertOneAsync(doc);
        }
        catch (Exception ex)
        {
            // MongoDB write failure must NEVER bubble up and fail the user's HTTP request
            logger.LogWarning(ex, "MongoDB audit failed for {Action} on #{Id}", action, feedbackId);
        }
    }

    public async Task<IEnumerable<AuditLogDto>> GetLogsAsync(int feedbackId)
    {
        // Builders<T> is MongoDB's query/sort DSL – equivalent to LINQ for SQL
        var filter = Builders<FeedbackAuditDocument>.Filter.Eq(d => d.FeedbackId, feedbackId);
        var sort   = Builders<FeedbackAuditDocument>.Sort.Descending(d => d.Timestamp);
        var docs   = await Collection.Find(filter).Sort(sort).ToListAsync();
        return docs.Select(ToDto);
    }
}
```

### MongoDB query DSL (Builders<T>)
MongoDB's .NET driver uses a typed builder pattern for filters and sorts:

```csharp
// Filter examples
var filter = Builders<T>.Filter.Eq(d => d.Action, "Delete");           // WHERE Action = 'Delete'
var filter = Builders<T>.Filter.Gte(d => d.Timestamp, DateTime.UtcNow.AddDays(-7)); // >= last 7 days
var filter = Builders<T>.Filter.And(filter1, filter2);                  // AND
var filter = Builders<T>.Filter.Or(filter1, filter2);                   // OR
var filter = Builders<T>.Filter.Empty;                                  // match all

// Sort examples
var sort = Builders<T>.Sort.Descending(d => d.Timestamp);              // ORDER BY Timestamp DESC
var sort = Builders<T>.Sort.Ascending(d => d.FeedbackId);              // ORDER BY FeedbackId ASC

// Find options
collection.Find(filter).Sort(sort).Limit(50).Skip(0).ToListAsync();
```

### Program.cs registration
```csharp
// MongoClient is a heavyweight singleton (connection pool)
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient("mongodb://localhost:27017"));

// IMongoDatabase is a lightweight reference, safe as Scoped
builder.Services.AddScoped<IMongoDatabase>(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase("FeedbackDB"));

builder.Services.AddScoped<IAuditService, MongoAuditService>();
```

### appsettings.json
```json
{
  "ConnectionStrings": {
    "MongoDB": "mongodb://localhost:27017"
  },
  "MongoDB": {
    "Database": "FeedbackDB"
  }
}
```

### Important: ObjectId explained
```
ObjectId = "65f3a1b2c3d4e5f6a7b8c9d0"  (24 hex chars = 12 bytes)
           ├───────┤ ├──────────────┤ ├──────┤
           timestamp  random machine     counter
           (4 bytes)   + process ID     (3 bytes)
           (Unix secs)  (5 bytes)

// Extract timestamp from ObjectId:
var id = new ObjectId("65f3a1b2c3d4e5f6a7b8c9d0");
DateTime created = id.CreationTime;  // UTC datetime when document was inserted
```

---

## 7. Elasticsearch – Full-Text Search

### What Elasticsearch is
Elasticsearch is a search engine built on Apache Lucene. It stores documents in an **inverted index** – the opposite of a database table:
- **SQL table**: row → columns (find row, then read columns)
- **ES inverted index**: term → list of document IDs (find term, then retrieve docs)

This makes full-text search **orders of magnitude faster** than SQL `LIKE '%term%'`.

### Why SQL LIKE fails at scale

```sql
-- SQL: full table scan, cannot use any index
SELECT * FROM Feedbacks WHERE Comment LIKE '%delivery problem%'
 
-- This gets slower as data grows: 100K rows → 100K comparisons
```

```
-- Elasticsearch: inverted index lookup, sub-millisecond
GET /feedback/_search?q=delivery+problem

-- "delivery" → [doc#1, doc#45, doc#99]
-- "problem"  → [doc#3, doc#45, doc#207]
-- Result     → [doc#45] (in both lists = best match)
```

### The FeedbackSearchDocument

```csharp
// Models/FeedbackSearchDocument.cs
// This is the shape stored in Elasticsearch, not the SQL entity.
// ES infers the mapping (field types) from this class on first index write.
public class FeedbackSearchDocument
{
    public int Id { get; set; }          // ES document _id = SQL primary key (no duplicates on re-index)
    public string Comment { get; set; }  // ← analysed text: tokenised, lowercased, stemmed
    public string Category { get; set; } // ← keyword: exact match only
    public string Sentiment { get; set; }// ← keyword
    public string CustomerName { get; set; }
    public int Rating { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Region { get; set; }
    public bool IsResolved { get; set; }
}
```

**CS field types** mapped by ES:
- `string` → `text` (analysed: tokenised for full-text) or `keyword` (exact match)
- `int` → `integer`
- `DateTime` → `date`
- `bool` → `boolean`

ES auto-detects the mapping on first index. To customise (e.g. force `category` to `keyword`), create the index with an explicit mapping before indexing.

### ElasticsearchService – full implementation

```csharp
public class ElasticsearchService(
    ElasticsearchClient client,
    ILogger<ElasticsearchService> logger) : ISearchService
{
    private const string IndexName = "feedback";

    // ── INDEX: called after Create and Update ────────────────────────────────
    // Uses the SQL PK as the ES document _id.
    // Re-indexing an existing _id = upsert (update in place) – no duplicates.
    public async Task IndexAsync(FeedbackResponseDto feedback)
    {
        var doc = ToSearchDocument(feedback);
        var response = await client.IndexAsync(doc,
            i => i.Index(IndexName).Id(doc.Id.ToString()));
        // Id = "42" → ES document _id = "42"
    }

    // ── BULK INDEX: one HTTP round-trip for N documents ──────────────────────
    // 120 individual IndexAsync calls = 120 network round-trips
    // 1 BulkAsync call = 1 network round-trip  ← far more efficient
    public async Task BulkIndexAsync(IEnumerable<FeedbackResponseDto> items)
    {
        var docs = items.Select(ToSearchDocument).ToList();
        await client.BulkAsync(b => b
            .Index(IndexName)
            .IndexMany(docs, (op, doc) => op.Id(doc.Id.ToString())));
    }

    // ── SEARCH: Bool/Should = multi-field search with scoring ────────────────
    public async Task<SearchResultDto> SearchAsync(string query, int page = 1, int pageSize = 20)
    {
        var response = await client.SearchAsync<FeedbackSearchDocument>(s => s
            .Indices(IndexName)
            .From((page - 1) * pageSize)
            .Size(pageSize)
            .Query(q => q
                .Bool(b => b
                    .Should(
                        // Boost(2.0f) = comment hits rank twice as high as other field hits
                        s => s.Match(m => m.Field(fd => fd.Comment)
                                          .Query(query)
                                          .Fuzziness(new Fuzziness("AUTO"))
                                          .Boost(2.0f)),
                        s => s.Match(m => m.Field(fd => fd.CustomerName)
                                          .Query(query)
                                          .Fuzziness(new Fuzziness("AUTO"))),
                        s => s.Match(m => m.Field(fd => fd.Category).Query(query)),
                        s => s.Match(m => m.Field(fd => fd.Region).Query(query))
                    )
                    .MinimumShouldMatch(1)  // at least ONE field must match
                )
            )
            .Highlight(h => h
                .PreTags("<mark>")      // wrap matching text in HTML mark tags
                .PostTags("</mark>")
                .Fields(hf => hf
                    .Add(new Field("comment"),
                         new HighlightField { NumberOfFragments = 3 })
                )
            )
        );

        // response.Hits = IReadOnlyCollection<Hit<FeedbackSearchDocument>>
        // hit.Source   = the FeedbackSearchDocument
        // hit.Score    = relevance score (higher = better match)
        // hit.Highlight = Dictionary<fieldName, List<string fragment>>
        var hits = response.Hits.Select(h =>
        {
            string? highlight = null;
            if (h.Highlight?.TryGetValue("comment", out var frags) == true)
                highlight = string.Join(" … ", frags);

            return new SearchHit
            {
                Item      = MapToDto(h.Source!),
                Score     = h.Score ?? 0,
                Highlight = highlight   // e.g.: "the <mark>delivery</mark> was late"
            };
        });

        return new SearchResultDto
        {
            Query     = query,
            TotalHits = response.HitsMetadata?.Total?.Match(t => t?.Value ?? 0L, l => l) ?? 0L,
            Page      = page,
            PageSize  = pageSize,
            Hits      = hits.ToList()
        };
    }
}
```

### Key Elasticsearch concepts explained

#### Fuzziness (AUTO)
```
Query "prblm" → matches "problem"
Query "delivry" → matches "delivery"
AUTO means:
  0-2 chars  → exact match
  3-5 chars  → 1 edit allowed
  6+ chars   → 2 edits allowed
An "edit" = insert / delete / substitute / transpose 1 character
```

#### Boost (relevance multiplier)
```
.Boost(2.0f) on comment field means:
  "the broken screen" in comment → score = 3.2 (base) × 2.0 = 6.4
  "the broken screen" in region  → score = 3.2 (base) × 1.0 = 3.2
  → comment matches rank first in results
```

#### Highlight
```
Input:  "There was a delivery problem with my order"
Query:  "delivery problem"
Output: "There was a <mark>delivery</mark> <mark>problem</mark> with my order"
```
The frontend renders the `<mark>` tags as yellow highlights.

#### MinimumShouldMatch(1)
```
Bool.Should = OR logic by default (0 matches = valid).
MinimumShouldMatch(1) = at least one field must match.
Without this, ALL documents would match (zero Should clauses satisfied is still valid).
```

### Program.cs registration
```csharp
var esSettings = new ElasticsearchClientSettings(new Uri("http://localhost:9200"))
    .DefaultIndex("feedback");  // used when Index not specified on a request

builder.Services.AddSingleton(new ElasticsearchClient(esSettings));
builder.Services.AddScoped<ISearchService, ElasticsearchService>();
```

### Initial seeding (Program.cs startup)
```csharp
// After SQL seed, bulk-index into Elasticsearch
using (var scope = app.Services.CreateScope())
{
    var db         = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
    await SeedData.SeedAsync(db);

    var searchSvc = scope.ServiceProvider.GetRequiredService<ISearchService>();
    var allItems  = await db.Feedbacks.AsNoTracking().ToListAsync();
    var dtos      = allItems.Select(f => new FeedbackResponseDto { /* map */ });
    await searchSvc.BulkIndexAsync(dtos);  // one HTTP call for 120 docs
}
```

### Why the document _id equals the SQL PK
When we update a feedback record:
```
SQL:  UPDATE Feedbacks SET Rating = 5 WHERE Id = 42
ES:   PUT /feedback/_doc/42  { ...new document... }
```
Using `"42"` as the ES `_id` means re-indexing the same feedback is an **upsert** – it replaces the existing document. There will never be duplicate ES documents for the same feedback ID.

---

## 8. New Endpoints Added

### GET /api/search?q={query}&page=1&pageSize=20 → Elasticsearch

**Query:**
```
GET http://localhost:5000/api/search?q=delivery+problem
```

**Response:**
```json
{
  "query": "delivery problem",
  "totalHits": 4,
  "page": 1,
  "pageSize": 20,
  "hits": [
    {
      "item": {
        "id": 45,
        "customerName": "Alice Smith",
        "category": "Delivery",
        "rating": 1,
        "comment": "There was a delivery problem with my order"
      },
      "score": 6.382,
      "highlight": "There was a <mark>delivery</mark> <mark>problem</mark> with my order"
    }
  ]
}
```

### GET /api/audit/feedback/{id} → MongoDB

**Query:**
```
GET http://localhost:5000/api/audit/feedback/42
```

**Response:**
```json
[
  {
    "id": "65f3a1b2c3d4e5f6a7b8c9d0",
    "action": "Update",
    "feedbackId": 42,
    "timestamp": "2024-03-14T10:22:33Z",
    "changeSummary": "Feedback #42 updated",
    "beforeJson": "{\"rating\":2,\"isResolved\":false}",
    "afterJson": "{\"rating\":4,\"isResolved\":true}"
  },
  {
    "id": "65f3a0b1c2d3e4f5a6b7c8d9",
    "action": "Create",
    "feedbackId": 42,
    "timestamp": "2024-03-14T09:00:00Z",
    "changeSummary": "New feedback #42 created",
    "beforeJson": null,
    "afterJson": "{\"rating\":2,\"comment\":\"Terrible product\"}"
  }
]
```

### GET /api/audit/recent?limit=50 → MongoDB

Returns the 50 most recent audit events across all records.

---

## 9. Running All Four Services Locally

### Docker Compose (recommended)

Create `docker-compose.yml` in the project root:

```yaml
version: "3.9"
services:

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    command: redis-server --maxmemory 256mb --maxmemory-policy allkeys-lru

  mongodb:
    image: mongo:7
    ports:
      - "27017:27017"
    environment:
      MONGO_INITDB_DATABASE: FeedbackDB
    volumes:
      - mongo_data:/data/db

  elasticsearch:
    image: docker.elastic.co/elasticsearch/elasticsearch:8.12.0
    ports:
      - "9200:9200"
      - "9300:9300"
    environment:
      - discovery.type=single-node
      - xpack.security.enabled=false    # disable auth for local dev
      - ES_JAVA_OPTS=-Xms512m -Xmx512m  # limit RAM for dev
    volumes:
      - es_data:/usr/share/elasticsearch/data

volumes:
  mongo_data:
  es_data:
```

**Start all services:**
```bash
docker compose up -d
```

**Verify each service is running:**
```bash
# Redis
redis-cli ping              # → PONG

# MongoDB
mongosh --eval "db.runCommand({ connectionStatus: 1 })"

# Elasticsearch
curl http://localhost:9200  # → { "name": "...", "cluster_name": "..." }
```

**Start the API:**
```bash
cd FeedbackAPI
dotnet run --urls "http://localhost:5000"
```

### Connection strings

Edit `appsettings.json` (already configured):
```json
{
  "ConnectionStrings": {
    "Redis":         "localhost:6379",
    "MongoDB":       "mongodb://localhost:27017",
    "Elasticsearch": "http://localhost:9200"
  },
  "MongoDB": {
    "Database": "FeedbackDB"
  }
}
```

### Verify everything works

```bash
# 1. SQL query (always works, no external service needed)
curl http://localhost:5000/api/feedback?page=1&pageSize=5

# 2. Redis-cached summary (first call computes from SQL, subsequent calls return from Redis)
curl http://localhost:5000/api/feedback/summary
# Run twice: second call should be measurably faster

# 3. Elasticsearch search (requires ES running)
curl "http://localhost:5000/api/search?q=broken"

# 4. MongoDB audit log (requires MongoDB running; create a feedback first)
curl -X POST http://localhost:5000/api/feedback \
  -H "Content-Type: application/json" \
  -d '{"customerName":"Test","rating":2,"comment":"broken item","category":1}'
curl http://localhost:5000/api/audit/recent
```

### Graceful degradation (when a service is NOT running)

| Service down | Effect on API |
|---|---|
| Redis down | Cache is skipped; every request computes from SQL. Slower but functional. |
| MongoDB down | Audit logs are not written. Warning logged. CRUD still works normally. |
| Elasticsearch down | Search returns empty results. Warning logged. CRUD still works normally. |
| SQL down | API is broken. SQL is the primary store – no fallback. |

This is implemented by the `try/catch` in each secondary service method that logs warnings and swallows exceptions.

---

## 10. Design Decisions Explained

### Why not store everything in Elasticsearch?

ES is a **search index**, not a primary database:
- No transactions: a failed partial write leaves data inconsistent
- Eventual consistency: indexed data may take milliseconds to appear in search
- ES is not optimised for `UPDATE` (it deletes + reindexes the whole document)
- No foreign keys / referential integrity

Rule: **always write to SQL first**. ES is a derived view of your data.

### Why not use Redis as the primary store?

Redis is **ephemeral by default**:
- Data can evict when memory is full (policy: `allkeys-lru`)
- Server crash = data loss (unless persistence is configured)
- No query language: you can only GET/SET by key, not SQL-style filtering

Rule: Redis is a **caching layer only**, never a source of truth.

### Why MongoDB instead of SQL for audit logs?

| Audit log in SQL | Audit log in MongoDB |
|---|---|
| Need `AuditLogs` table with exact columns | Flexible document – store any JSON snapshot |
| Schema changes to `Feedback` may break inserts | Just serialize whatever object you have |
| `ALTER TABLE` if you add a field and want it audited | Zero migration needed |
| Good enough for simple auditing | Better for rapidly evolving entities |

The `BeforeJson` and `AfterJson` columns store serialized snapshots. If `CustomerFeedback` gains a new `Tags` field next month, the MongoDB audit captures it automatically with no schema change.

### Dual-write consistency risk

When `CreateAsync` runs:
```
1. SQL INSERT → succeeds → Id = 42
2a. ES index → (maybe fails, maybe succeeds)
2b. MongoDB insert → (maybe fails, maybe succeeds)
2c. Redis cache bust → (maybe fails, maybe succeeds)
```

The SQL is the only store that MUST succeed. The others are **eventually consistent**. This means:
- If ES fails, you can re-index from SQL later
- If MongoDB fails, you lose that audit event (acceptable for non-financial auditing)
- If Redis fails, next request re-computes from SQL

In banking or healthcare systems with strict audit requirements, you'd use a reliable message queue (Kafka / Azure Service Bus) to guarantee delivery to secondary stores.

### Task.WhenAll for parallel fan-out

```csharp
// Sequential: 3 network calls, total time = sum of all three → slow
await search.IndexAsync(result);
await audit.LogCreatedAsync(id, result);
await cache.RemoveAsync(key);

// Parallel: 3 network calls, total time = slowest of three → 3x faster
await Task.WhenAll(
    search.IndexAsync(result),
    audit.LogCreatedAsync(id, result),
    cache.RemoveAsync(key)
);
```

`Task.WhenAll` starts all three tasks simultaneously and waits for all to finish. Individual failures inside each task are caught internally so `Task.WhenAll` itself won't throw.

---

## 11. Interview Q&A

### Q: Why use four databases instead of one?

> "Each database is optimised for a different access pattern. SQL is best for structured CRUD and aggregations. Redis is best for sub-millisecond cache reads. MongoDB is best for flexible append-only documents. Elasticsearch is best for full-text, fuzzy, highlighted search. Using the right tool for each job is called polyglot persistence."

### Q: What happens if Elasticsearch goes down?

> "Our `ElasticsearchService` wraps every call in `try/catch`. On failure, it logs a warning and returns an empty result. The main CRUD endpoints (backed by SQL) are completely unaffected. The search endpoint returns empty results with no error. When Elasticsearch comes back, the next re-index call (on any create/update) will repopulate it."

### Q: How do you keep Elasticsearch in sync with SQL?

> "We use dual-write: when a record is created, updated, or deleted in SQL, we also call `search.IndexAsync()` or `search.DeleteAsync()`. Because the ES document `_id` equals the SQL primary key, a re-index is always an upsert – no duplicate documents. For initial seeding, `BulkIndexAsync` reads from SQL and sends all 120 documents in one HTTP call."

### Q: Why is IMongoClient a Singleton but IMongoDatabase is Scoped?

> "`IMongoClient` manages a connection pool – it's expensive to create and must be reused across requests. `IMongoDatabase` is a lightweight reference object (it just holds the database name) so it's safe to create per request. If you registered `IMongoClient` as Scoped you'd create a new connection pool on every HTTP request, exhausting TCP connections quickly."

### Q: How does the cache-aside pattern work?

> "Cache-aside means the application controls the cache. On a GET request: check Redis first. If data is there (HIT), return it immediately. If not (MISS), fetch from SQL, store the result in Redis with a TTL, and return it. On write (POST/PUT/DELETE): write to SQL, then delete the relevant Redis keys so the next read re-fetches fresh data. This is the most common caching pattern in web APIs."

### Q: What is an Elasticsearch inverted index?

> "A regular database index maps row → value. An inverted index maps value → list of documents. So for the word 'delivery', ES has an entry pointing to all document IDs that contain 'delivery'. A search for 'delivery' becomes a direct lookup into this structure rather than a full table scan."

### Q: What's the difference between text and keyword in Elasticsearch?

> "`text` fields are analysed: the content is tokenised, lowercased, and stemmed before indexing. 'Broken Screen' becomes ['broken', 'screen']. This enables full-text search. `keyword` fields are stored literally and only support exact-match filtering. 'ProductQuality' stays as 'ProductQuality'. In our mapping, `comment` is `text` (analysed), `category` and `sentiment` are `keyword` (exact match for filtering)."

### Q: What is a TTL in Redis and why does it matter?

> "TTL = Time To Live. After the TTL expires, Redis automatically deletes the key. Without a TTL, cached data would never expire and you'd serve stale dashboard numbers indefinitely. We use 60 seconds for summary (dashboard refreshes every minute are acceptable) and 5 minutes for trends (expensive computation, low change frequency)."

### Q: How would you handle the case where the audit log MUST be written (compliance)?

> "In the current design, audit failures are swallowed. For strict compliance (PCI-DSS, GDPR, banking), you'd use a message queue (Kafka, Azure Service Bus) as an intermediary. The SQL write succeeds, then a message is published to a queue. A separate background service consumes from the queue and writes to MongoDB. The queue provides durability guarantees – messages are not lost even if MongoDB is temporarily down."
