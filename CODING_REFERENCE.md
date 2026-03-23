# Feedback Dashboard – Complete Coding Reference
### (For a 1-Hour Coding Assessment – No Internet Needed)

---

## TABLE OF CONTENTS
1. [Architecture Overview](#1-architecture-overview)
2. [Project Structure](#2-project-structure)
3. [Backend – C# .NET 8 Web API](#3-backend--c-net-8-web-api)
   - [Program.cs bootstrap pattern](#31-programcs--the-entire-bootstrap)
   - [Model & Enums](#32-model--enums)
   - [DTOs](#33-dtos--what-they-are-and-why)
   - [EF Core DbContext](#34-ef-core-dbcontext)
   - [Service Interface + Implementation](#35-service-interface--implementation)
   - [Controller](#36-controller)
4. [Async / Await – Complete Explanation](#4-async--await--complete-explanation)
5. [EF Core Key Patterns](#5-ef-core-key-patterns)
6. [Performance Optimizations (Backend)](#6-performance-optimizations-backend)
7. [Angular – Complete Guide for MVC Developers](#7-angular--complete-guide-for-mvc-developers)
   - [Mental model shift from MVC](#71-mental-model-shift-from-aspnet-mvc)
   - [Project Structure](#72-angular-project-structure)
   - [Bootstrap – app.config.ts](#73-bootstrap--appconfigts)
   - [Routing](#74-routing)
   - [Models / Interfaces](#75-models--interfaces)
   - [Services + HttpClient](#76-services--httpclient)
   - [Components – full anatomy](#77-components--full-anatomy)
   - [Template syntax cheat sheet](#78-template-syntax-cheat-sheet)
   - [Parent → Child data flow](#79-parent--child-data-flow-input--output)
   - [forkJoin – parallel HTTP calls](#710-forkjoin--parallel-http-calls)
   - [Chart.js / ng2-charts usage](#711-chartjs--ng2-charts)
8. [API ↔ Angular Contract Summary](#8-api--angular-contract-summary)
9. [Common Mistakes & How to Avoid Them](#9-common-mistakes--how-to-avoid-them)
10. [60-Minute Build Checklist](#10-60-minute-build-checklist)

---

## 1. Architecture Overview

```
Browser (Angular SPA – port 4200)
         │  HTTP/JSON
         ▼
  FeedbackAPI (.NET 8 Web API – port 5000)
         │
         ▼
  EF Core (In-Memory DB in dev; SQL Server in prod)
```

**Request flow for the dashboard:**
1. Angular component calls `FeedbackService` (a typed HTTP wrapper).
2. `FeedbackService` sends HTTP GET/POST/PUT/DELETE to `http://localhost:5000/api/feedback/...`.
3. The .NET `FeedbackController` receives the call, delegates to `IFeedbackService`.
4. `FeedbackService` queries EF Core using LINQ, returns DTOs.
5. Angular component binds the response data to the template.

**No sessions, no server-side rendering.** Everything is JSON over HTTP.

---

## 2. Project Structure

```
DotNetInterview/
├── FeedbackAPI/                         ← .NET 8 Web API
│   ├── Program.cs                       ← DI registration + middleware pipeline
│   ├── FeedbackAPI.csproj              ← NuGet packages
│   ├── Models/
│   │   └── CustomerFeedback.cs         ← EF entity + enums
│   ├── DTOs/
│   │   └── FeedbackDtos.cs             ← Create/Update/Response/Summary/Trend/Filter/Paged
│   ├── Data/
│   │   ├── FeedbackDbContext.cs        ← EF Core DbContext
│   │   └── SeedData.cs                 ← 120 sample records
│   ├── Services/
│   │   ├── IFeedbackService.cs         ← Interface (contract)
│   │   └── FeedbackService.cs          ← Implementation
│   └── Controllers/
│       └── FeedbackController.cs       ← HTTP endpoints
│
└── feedback-dashboard/                  ← Angular 21 SPA
    └── src/app/
        ├── app.config.ts               ← Bootstrap (providers)
        ├── app.routes.ts               ← Routing
        ├── app.ts                      ← Root component (just <router-outlet>)
        ├── models/
        │   └── feedback.models.ts      ← TypeScript interfaces (mirror the DTOs)
        ├── services/
        │   └── feedback.service.ts     ← HttpClient wrapper
        └── components/
            ├── dashboard/              ← Main page (orchestrator)
            ├── filter-panel/           ← Emits filter changes up to dashboard
            ├── kpi-cards/              ← Receives summary, renders KPI numbers
            ├── trend-chart/            ← Line chart (Chart.js)
            ├── sentiment-chart/        ← Doughnut chart
            ├── category-chart/         ← Bar chart
            └── feedback-list/          ← Paginated table
```

---

## 3. Backend – C# .NET 8 Web API

### 3.1 Program.cs – The Entire Bootstrap

**Key concept:** In .NET 8 (minimal hosting), there's no `Startup.cs`. Everything is in `Program.cs`.  
There are two phases: **register services** (`builder.Services.Add...`) then **build the pipeline** (`app.Use...`).

```csharp
var builder = WebApplication.CreateBuilder(args);   // Phase 1 starts

// Register services into the DI container
builder.Services.AddControllers();
builder.Services.AddDbContext<FeedbackDbContext>(opt =>
    opt.UseInMemoryDatabase("FeedbackDb"));          // swap to UseSqlServer() for prod
builder.Services.AddScoped<IFeedbackService, FeedbackService>();
builder.Services.AddResponseCaching();

// CORS – MUST be configured before app.UseCors() below
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyMethod()
              .AllowAnyHeader());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();            // Phase 2 starts – middleware order matters!

// Seed data before any request arrives
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
    await SeedData.SeedAsync(db);     // async at top level is fine in .NET 8
}

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.UseCors("AllowAngular");          // ← must come BEFORE MapControllers
app.UseResponseCaching();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();                 // ← maps all [ApiController] classes

app.Run();
```

**DI lifetime rules (important):**
| Lifetime | Means | Use for |
|---|---|---|
| `AddSingleton` | One instance ever | Config, caches |
| `AddScoped` | One per HTTP request | DbContext, services |
| `AddTransient` | New every time injected | Lightweight, stateless helpers |

Always use **`AddScoped`** for EF Core DbContext and application services.

---

### 3.2 Model & Enums

```csharp
namespace FeedbackAPI.Models;

// Enums become integers in the DB (0, 1, 2...) but you always use the name in code
public enum FeedbackCategory { ProductQuality, CustomerService, Pricing, Delivery, Website, Other }
public enum SentimentType    { Positive, Neutral, Negative }

public class CustomerFeedback
{
    public int Id { get; set; }                        // EF auto-generates PK
    public string CustomerName { get; set; } = string.Empty;  // init to avoid null warning
    public string? CustomerEmail { get; set; }         // ? = nullable (can be null)
    public FeedbackCategory Category { get; set; }
    public SentimentType Sentiment { get; set; }       // AUTO-INFERRED from Rating in service
    public int Rating { get; set; }                    // 1–5
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;  // default to now
    public string? ProductId { get; set; }
    public string? Region { get; set; }
    public bool IsResolved { get; set; } = false;
}
```

**Key point:** `Sentiment` is never set by the caller. The service infers it: rating ≥ 4 → Positive, 3 → Neutral, ≤ 2 → Negative.

---

### 3.3 DTOs – What They Are and Why

**DTO = Data Transfer Object.** You never expose your entity directly to the API caller.  
Reasons: security (hide internal fields), shape (serialize enums as strings, not ints), validation.

```
FeedbackCreateDto    ← what the caller POSTs (no Id, no Sentiment – those are server-side)
FeedbackUpdateDto    ← what the caller PUTs (all fields nullable = partial update)
FeedbackResponseDto  ← what the API returns (Category & Sentiment as strings, not ints)
FeedbackSummaryDto   ← aggregated stats
TrendDataPointDto    ← one point on a chart
FeedbackFilterParams ← [FromQuery] bound from URL parameters for filtering
PagedResult<T>       ← generic wrapper: { data, totalCount, page, pageSize, totalPages }
```

**PagedResult pattern – memorize this:**
```csharp
public class PagedResult<T>
{
    public IEnumerable<T> Data { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize); // computed
}
```

**FeedbackUpdateDto uses nullable types for partial update:**
```csharp
public class FeedbackUpdateDto
{
    public FeedbackCategory? Category { get; set; }  // ? means "only update if provided"
    public int? Rating { get; set; }
    public string? Comment { get; set; }
    public bool? IsResolved { get; set; }
}
```
In the service: `if (dto.Category.HasValue) entity.Category = dto.Category.Value;`

---

### 3.4 EF Core DbContext

```csharp
// Primary constructor syntax (C# 12 / .NET 8) – passes options to base DbContext
public class FeedbackDbContext(DbContextOptions<FeedbackDbContext> options) : DbContext(options)
{
    // DbSet = the "table" – accessed as db.Feedbacks
    public DbSet<CustomerFeedback> Feedbacks => Set<CustomerFeedback>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerFeedback>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.Property(f => f.CustomerName).IsRequired().HasMaxLength(150);
            entity.Property(f => f.Comment).IsRequired().HasMaxLength(2000);
            entity.HasIndex(f => f.CreatedAt);     // index for date-range queries
            entity.HasIndex(f => f.Category);
            entity.HasIndex(f => f.Sentiment);
        });
    }
}
```

**How EF Core works:**
- You write LINQ → EF translates to SQL
- `db.Feedbacks` = table, `.Where(...)` = WHERE, `.OrderBy(...)` = ORDER BY
- `await db.SaveChangesAsync()` = commit all pending changes (INSERT/UPDATE/DELETE)

---

### 3.5 Service Interface + Implementation

**Why an interface?**  
Testability (mock it), dependency inversion (swap implementations without changing callers).

```csharp
public interface IFeedbackService
{
    Task<PagedResult<FeedbackResponseDto>> GetAllAsync(FeedbackFilterParams filter);
    Task<FeedbackResponseDto?> GetByIdAsync(int id);           // ? = may return null
    Task<FeedbackResponseDto> CreateAsync(FeedbackCreateDto dto);
    Task<FeedbackResponseDto?> UpdateAsync(int id, FeedbackUpdateDto dto);
    Task<bool> DeleteAsync(int id);
    Task<FeedbackSummaryDto> GetSummaryAsync(FeedbackFilterParams filter);
    Task<IEnumerable<TrendDataPointDto>> GetTrendsAsync(DateTime from, DateTime to, string groupBy);
}
```

**Service implementation – key patterns:**

```csharp
public class FeedbackService(FeedbackDbContext db) : IFeedbackService
{
    // ── Pattern 1: Sentinel inference with switch expression ──────────────────
    private static SentimentType InferSentiment(int rating) => rating switch
    {
        >= 4 => SentimentType.Positive,
        3    => SentimentType.Neutral,
        _    => SentimentType.Negative       // _ = default case
    };

    // ── Pattern 2: Mapping entity → DTO (static helper) ──────────────────────
    private static FeedbackResponseDto ToDto(CustomerFeedback f) => new()
    {
        Id           = f.Id,
        Category     = f.Category.ToString(),  // enum → string for JSON
        Sentiment    = f.Sentiment.ToString(),
        // ... rest of fields
    };

    // ── Pattern 3: Dynamic filter building ───────────────────────────────────
    private IQueryable<CustomerFeedback> ApplyFilters(IQueryable<CustomerFeedback> query,
                                                       FeedbackFilterParams filter)
    {
        if (filter.From.HasValue)     query = query.Where(f => f.CreatedAt >= filter.From.Value);
        if (filter.Category.HasValue) query = query.Where(f => f.Category == filter.Category.Value);
        // ...each filter only applied if present
        return query;
    }

    // ── Pattern 4: Paginated query ────────────────────────────────────────────
    public async Task<PagedResult<FeedbackResponseDto>> GetAllAsync(FeedbackFilterParams filter)
    {
        var query = ApplyFilters(db.Feedbacks.AsNoTracking(), filter)
                        .OrderByDescending(f => f.CreatedAt);

        var total = await query.CountAsync();           // COUNT(*) – no data fetched
        var data  = await query
            .Skip((filter.Page - 1) * filter.PageSize) // OFFSET
            .Take(filter.PageSize)                      // FETCH NEXT n ROWS
            .Select(f => ToDto(f))
            .ToListAsync();                             // executes the SQL

        return new PagedResult<FeedbackResponseDto>
            { Data = data, TotalCount = total, Page = filter.Page, PageSize = filter.PageSize };
    }

    // ── Pattern 5: FindAsync for updates (tracks the entity) ──────────────────
    public async Task<FeedbackResponseDto?> UpdateAsync(int id, FeedbackUpdateDto dto)
    {
        var entity = await db.Feedbacks.FindAsync(id);  // tracked = EF watches changes
        if (entity is null) return null;

        if (dto.Category.HasValue)   entity.Category   = dto.Category.Value;
        if (dto.IsResolved.HasValue) entity.IsResolved = dto.IsResolved.Value;
        if (dto.Rating.HasValue)
        {
            entity.Rating    = Math.Clamp(dto.Rating.Value, 1, 5);
            entity.Sentiment = InferSentiment(entity.Rating); // re-infer after rating change
        }

        await db.SaveChangesAsync();  // EF detects changes and generates UPDATE
        return ToDto(entity);
    }

    // ── Pattern 6: GroupBy in-memory for trends ───────────────────────────────
    public async Task<IEnumerable<TrendDataPointDto>> GetTrendsAsync(
        DateTime from, DateTime to, string groupBy)
    {
        // Fetch the range first (EF WHERE), then group in C# (switch expression)
        var data = await db.Feedbacks.AsNoTracking()
            .Where(f => f.CreatedAt >= from && f.CreatedAt <= to)
            .ToListAsync();

        IEnumerable<IGrouping<string, CustomerFeedback>> groups = groupBy.ToLower() switch
        {
            "week"  => data.GroupBy(f => $"{f.CreatedAt.Year}-W{ISOWeek.GetWeekOfYear(f.CreatedAt):D2}"),
            "month" => data.GroupBy(f => f.CreatedAt.ToString("yyyy-MM")),
            _       => data.GroupBy(f => f.CreatedAt.ToString("yyyy-MM-dd"))
        };

        return groups.OrderBy(g => g.Key).Select(g => new TrendDataPointDto
        {
            Date          = g.Key,
            Count         = g.Count(),
            AverageRating = g.Average(f => f.Rating),
            PositiveCount = g.Count(f => f.Sentiment == SentimentType.Positive),
            NegativeCount = g.Count(f => f.Sentiment == SentimentType.Negative)
        });
    }
}
```

---

### 3.6 Controller

```csharp
[ApiController]                  // enables automatic 400 for invalid models, binding
[Route("api/[controller]")]      // [controller] = "feedback" (class name minus "Controller")
[Produces("application/json")]
public class FeedbackController(IFeedbackService service) : ControllerBase
{
    // Primary constructor injection – C# 12 feature

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] FeedbackFilterParams filter)
    {
        var result = await service.GetAllAsync(filter);
        return Ok(result);                              // 200 + JSON body
    }

    [HttpGet("{id:int}")]                               // route constraint :int
    public async Task<IActionResult> GetById(int id)
    {
        var item = await service.GetByIdAsync(id);
        return item is null ? NotFound() : Ok(item);   // 404 or 200
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FeedbackCreateDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var created = await service.CreateAsync(dto);
        // CreatedAtAction = 201 + Location header pointing to GetById
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] FeedbackUpdateDto dto)
    {
        var updated = await service.UpdateAsync(id, dto);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await service.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();     // 204 (success, no body) or 404
    }

    [HttpGet("summary")]          // → GET /api/feedback/summary
    public async Task<IActionResult> GetSummary([FromQuery] FeedbackFilterParams filter)
        => Ok(await service.GetSummaryAsync(filter));

    [HttpGet("trends")]           // → GET /api/feedback/trends?from=...&to=...&groupBy=day
    public async Task<IActionResult> GetTrends(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string groupBy = "day")
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);  // ?? = null-coalescing
        var end   = to   ?? DateTime.UtcNow;
        if (start > end) return BadRequest("'from' must be before 'to'.");

        return Ok(await service.GetTrendsAsync(start, end, groupBy));
    }

    [HttpGet("categories")]
    public IActionResult GetCategories() => Ok(Enum.GetNames<FeedbackCategory>());
}
```

**Return types cheat sheet:**
| Method | HTTP Status | Use when |
|---|---|---|
| `Ok(obj)` | 200 | Successful GET / PUT |
| `Created(url, obj)` | 201 | New resource created |
| `CreatedAtAction(...)` | 201 + Location header | POST creates resource |
| `NoContent()` | 204 | DELETE / PUT with no body |
| `NotFound()` | 404 | Resource doesn't exist |
| `BadRequest(msg)` | 400 | Invalid input |

---

## 4. Async / Await – Complete Explanation

### What it actually does

ASP.NET handles many requests on a **thread pool**. Without async, a thread is *blocked* (doing nothing) waiting for the database to respond. With async, the thread is *released* back to the pool while waiting, so it can serve other requests.

```
Without async:  Thread → [waiting for DB... doing nothing... ] → response
With async:     Thread → releases back to pool → [DB does its thing] → thread reclaimed → response
```

The CPU work is the same. It's about **not wasting threads** while I/O happens.

### The Rules

**Rule 1: async method must return `Task` or `Task<T>`** (not `void`)
```csharp
public async Task<string> GetNameAsync()   // Task<T> when you return something
public async Task DoWorkAsync()            // Task when nothing to return
```

**Rule 2: `await` can only be used inside an `async` method**
```csharp
public async Task<FeedbackResponseDto> CreateAsync(FeedbackCreateDto dto)
{
    db.Feedbacks.Add(entity);
    await db.SaveChangesAsync();   // releases thread while SQL executes
    return ToDto(entity);          // resumes here after DB responds
}
```

**Rule 3: await "unwraps" the Task**
```csharp
Task<List<Feedback>> task = db.Feedbacks.ToListAsync();  // a promise
List<Feedback> list = await task;                         // the actual list
// OR in one line:
var list = await db.Feedbacks.ToListAsync();
```

**Rule 4: async is contagious – it bubbles up**
```csharp
// If service is async, controller must also be async
public async Task<IActionResult> GetAll()
{
    var result = await service.GetAllAsync(); // must await
    return Ok(result);
}
```

**Rule 5: Never `.Result` or `.Wait()` on a task** – that blocks the thread and causes deadlocks in ASP.NET.
```csharp
// WRONG – blocks the thread, can deadlock
var list = db.Feedbacks.ToListAsync().Result;

// CORRECT
var list = await db.Feedbacks.ToListAsync();
```

### EF Core async methods to memorize
```csharp
await db.Feedbacks.ToListAsync()           // SELECT all
await db.Feedbacks.FirstOrDefaultAsync()   // SELECT TOP 1, returns null if none
await db.Feedbacks.FindAsync(id)           // SELECT by PK (tracked)
await db.Feedbacks.CountAsync()            // COUNT(*)
await db.Feedbacks.AnyAsync(f => ...)      // EXISTS
await db.SaveChangesAsync()                // INSERT/UPDATE/DELETE commit
```

### ConfigureAwait
In ASP.NET Core, you generally **don't need** `ConfigureAwait(false)`. It matters in library code or WinForms. In a Web API, leave it off.

---

## 5. EF Core Key Patterns

### AsNoTracking – performance for reads
```csharp
// Without AsNoTracking: EF tracks every entity in a "change tracker"
// With AsNoTracking: EF skips that overhead – use for GET/read-only queries
db.Feedbacks.AsNoTracking().Where(...)
```

### FindAsync vs FirstOrDefaultAsync
```csharp
// FindAsync: searches the change tracker first, then DB. USE for UPDATE/DELETE
var entity = await db.Feedbacks.FindAsync(id);

// FirstOrDefaultAsync: always goes to DB. USE for GET (combine with AsNoTracking)
var entity = await db.Feedbacks.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
```

### Where chaining (builds SQL lazily)
```csharp
var query = db.Feedbacks.AsNoTracking();             // no SQL yet
query = query.Where(f => f.Category == cat);         // adds WHERE clause
query = query.OrderByDescending(f => f.CreatedAt);  // adds ORDER BY
var count = await query.CountAsync();                // executes COUNT(*)
var page  = await query.Skip(0).Take(20).ToListAsync(); // executes SELECT
```

### IQueryable vs IEnumerable
- `IQueryable<T>` = lazy, builds SQL → **use for database queries**
- `IEnumerable<T>` = in-memory → once you call `.ToList()`, you're in C# land
- Grouping (GroupBy on DateTime) must happen in-memory because SQL can't format dates the same way. Fetch first, then group.

### Skip/Take = Pagination
```csharp
int page = 1, pageSize = 20;
var items = await query
    .Skip((page - 1) * pageSize)   // skip previous pages
    .Take(pageSize)                 // take this page
    .ToListAsync();
```

### Dictionary aggregations (the Summary pattern)
```csharp
// After ToList(), you're in LINQ-to-Objects – GroupBy works on any property
CountByCategory = all.GroupBy(f => f.Category.ToString())
                     .ToDictionary(g => g.Key, g => g.Count())

AvgRatingByCategory = all.GroupBy(f => f.Category.ToString())
                         .ToDictionary(g => g.Key, g => g.Average(f => f.Rating))
```

---

## 6. Performance Optimizations (Backend)

### Already implemented in this project
| Optimization | Where | Why |
|---|---|---|
| `AsNoTracking()` | All GET queries | Skips change tracker overhead |
| Indexes on `CreatedAt`, `Category`, `Sentiment` | `OnModelCreating` | Speeds up WHERE/ORDER BY |
| Pagination (`Skip/Take`) | `GetAllAsync` | Never loads entire table |
| `Math.Clamp(rating, 1, 5)` | `CreateAsync`, `UpdateAsync` | Input safety at service layer |
| `AddResponseCaching()` | `Program.cs` | HTTP-level caching header support |

### How to add Redis caching (described in deployment doc)
```csharp
// Register (Program.cs)
builder.Services.AddStackExchangeRedisCache(opt =>
    opt.Configuration = "localhost:6379");

// Inject IDistributedCache into service, then:
var cached = await _cache.GetStringAsync(cacheKey);
if (cached != null) return JsonSerializer.Deserialize<FeedbackSummaryDto>(cached)!;

var result = await ComputeSummaryAsync(filter);
await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(result),
    new DistributedCacheEntryOptions
        { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
return result;
```

### Output Caching (.NET 8 built-in)
```csharp
builder.Services.AddOutputCache();
// ...
app.UseOutputCache();
// On controller action:
[OutputCache(Duration = 60)]
public async Task<IActionResult> GetSummary(...) { ... }
```

### Rate Limiting (.NET 8 built-in)
```csharp
builder.Services.AddRateLimiter(opt =>
    opt.AddFixedWindowLimiter("fixed", o => {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 100;
    }));
app.UseRateLimiter();
// On controller: [EnableRateLimiting("fixed")]
```

---

## 7. Angular – Complete Guide for MVC Developers

### 7.1 Mental Model Shift from ASP.NET MVC

| ASP.NET MVC | Angular Equivalent |
|---|---|
| Controller + View = one page | Component = one piece of UI (can nest many) |
| Razor `@Model.Property` | `{{ property }}` in template |
| `@Html.ActionLink(...)` | `routerLink="/path"` |
| `@foreach` in Razor | `@for (item of items; track item.id)` in template |
| `@if (condition)` | `@if (condition)` |
| `HttpContext.Request` | `HttpClient` service (injected) |
| `ViewBag` / `TempData` | Component `@Input()` / services |
| `ActionResult` | `Observable<T>` from HttpClient |
| URL routing in `RouteConfig.cs` | `app.routes.ts` |
| `_Layout.cshtml` | `app.ts` with `<router-outlet>` |
| `new HttpClient()` | `inject(HttpClient)` (DI) |

**Biggest difference:** In MVC, the server renders HTML. In Angular, the **browser** renders everything. The server only sends JSON.

---

### 7.2 Angular Project Structure

```
src/app/
├── app.config.ts     ← register HttpClient, Router (like Startup.cs)
├── app.routes.ts     ← URL → Component mapping (like RouteConfig.cs)
├── app.ts            ← root shell, just renders <router-outlet>
├── models/           ← TypeScript interfaces (like C# DTOs)
├── services/         ← HttpClient wrappers (like C# IFeedbackService)
└── components/       ← UI pieces (like MVC Views + mini-controllers)
```

---

### 7.3 Bootstrap – app.config.ts

```typescript
// This is Angular's equivalent of Program.cs / Startup.cs
import { ApplicationConfig } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(withFetch()),  // ← MUST have this for HttpClient to work
  ]
};
```

**If you forget `provideHttpClient()`**, you'll get a runtime error: "NullInjectorError: HttpClient".

---

### 7.4 Routing

```typescript
// app.routes.ts
import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',                    // matches "/"
    loadComponent: () =>         // lazy load (only downloads JS when user visits)
      import('./components/dashboard/dashboard')
        .then(m => m.DashboardComponent),
  },
  { path: '**', redirectTo: '' } // wildcard → home
];
```

**Eager vs Lazy:**
- `component: DashboardComponent` = always downloaded at startup (eager)
- `loadComponent: () => import(...)` = downloaded only when user navigates there (lazy = faster initial load)

---

### 7.5 Models / Interfaces

TypeScript interfaces are purely compile-time. They don't exist at runtime. They're like C# interfaces but no code generation needed.

```typescript
// feedback.models.ts
export type FeedbackCategory = 'ProductQuality' | 'CustomerService' | 'Pricing' | 'Delivery' | 'Website' | 'Other';
export type SentimentType = 'Positive' | 'Neutral' | 'Negative';

export interface FeedbackItem {
  id: number;
  customerName: string;
  customerEmail?: string;        // ? = optional (can be undefined)
  category: FeedbackCategory;
  sentiment: SentimentType;
  rating: number;
  comment: string;
  createdAt: string;             // dates come as ISO strings from JSON
  region?: string;
  isResolved: boolean;
}

export interface PagedResult<T> {   // generic, like C#'s PagedResult<T>
  data: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface FeedbackFilter {
  from?: string;
  to?: string;
  category?: FeedbackCategory;
  page?: number;
  pageSize?: number;
  // ...
}
```

---

### 7.6 Services + HttpClient

Angular services are singletons injected into components. `inject()` is the modern way (Angular 14+).

```typescript
@Injectable({ providedIn: 'root' })   // 'root' = singleton app-wide
export class FeedbackService {
  private readonly http = inject(HttpClient);           // inject instead of constructor param
  private readonly base = 'http://localhost:5000/api/feedback';

  // Returns Observable<T> – like Task<T> in C# but "push-based"
  getAll(filter: FeedbackFilter = {}): Observable<PagedResult<FeedbackItem>> {
    return this.http.get<PagedResult<FeedbackItem>>(this.base, {
      params: this.toParams(filter),  // convert filter object to ?key=value&key2=value2
    });
  }

  create(dto: FeedbackCreateDto): Observable<FeedbackItem> {
    return this.http.post<FeedbackItem>(this.base, dto);
  }

  update(id: number, dto: FeedbackUpdateDto): Observable<FeedbackItem> {
    return this.http.put<FeedbackItem>(`${this.base}/${id}`, dto);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  // Build HttpParams from an object (skipping undefined values)
  private toParams(filter: FeedbackFilter): HttpParams {
    let params = new HttpParams();
    Object.entries(filter).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    });
    return params;
  }
}
```

**Observable vs Promise (vs Task in C#):**
- `Observable` = stream, can emit multiple values, lazy (nothing happens until `.subscribe()`)
- `Promise` = one value, eager (starts immediately)
- `Task<T>` in C# ≈ `Promise<T>` in JS
- Angular HttpClient returns `Observable` — you always need to `.subscribe()` or use `async` pipe

```typescript
// How to consume in a component:
this.svc.getAll().subscribe({
  next: result => this.data = result,   // success
  error: err   => console.error(err),   // failure
});
```

---

### 7.7 Components – Full Anatomy

```typescript
// dashboard.ts
@Component({
  selector: 'app-dashboard',         // used as <app-dashboard> in templates
  standalone: true,                  // Angular 14+ – no NgModule needed
  imports: [                         // components/directives used in THIS component's template
    CommonModule,                    // gives @if, @for, etc. (sometimes not needed in v17+)
    FilterPanelComponent,
    KpiCardsComponent,
  ],
  templateUrl: './dashboard.html',   // external HTML file
  styleUrl: './dashboard.scss',      // external SCSS file
})
export class DashboardComponent implements OnInit {
  // Injected services (modern syntax)
  private readonly svc = inject(FeedbackService);

  // Component state (these become bindable in the template)
  summary: FeedbackSummary | null = null;
  trendData: TrendDataPoint[] = [];
  listLoading = false;

  // Lifecycle: called after component is created
  ngOnInit(): void {
    this.loadAll();
  }

  private loadAll(): void {
    this.svc.getSummary().subscribe({
      next: s => this.summary = s,
    });
  }
}
```

**Standalone components (Angular 14+):**  
Old Angular used `NgModule` (like ASP.NET Areas). New Angular is standalone — each component declares what it uses in its own `imports: []`. Think of it as each component being self-contained.

**Lifecycle hooks (most common):**
| Hook | When called | Use for |
|---|---|---|
| `ngOnInit()` | After component created + inputs set | Fetch data, initialize |
| `ngOnChanges()` | When `@Input()` values change | React to parent data changes |
| `ngOnDestroy()` | Before component removed | Unsubscribe, cleanup |

---

### 7.8 Template Syntax Cheat Sheet

```html
<!-- Property binding (one-way: component → template) -->
<input [value]="myProperty" />           <!-- like @Model.Property in Razor -->

<!-- Event binding (template → component) -->
<button (click)="doSomething()">Click</button>

<!-- Two-way binding (ngModel): needs FormsModule in imports -->
<input [(ngModel)]="filter.region" />    <!-- like @Html.EditorFor + model binding -->

<!-- Interpolation -->
<p>Total: {{ summary.totalCount }}</p>   <!-- like @Model.TotalCount in Razor -->

<!-- Angular 17+ Control Flow (@if, @for) -->
@if (summary) {
  <div>{{ summary.totalCount }}</div>
} @else {
  <div>Loading...</div>
}

@for (item of listResult.data; track item.id) {
  <tr>
    <td>{{ item.customerName }}</td>
    <td>{{ item.rating }}</td>
    <td>{{ item.createdAt | date:'mediumDate' }}</td>  <!-- pipe = format filter -->
  </tr>
}

<!-- Class binding -->
<span [class]="sentimentClass(item.sentiment)">{{ item.sentiment }}</span>
<button [class.active]="isActive">...</button>  <!-- adds 'active' class if true -->

<!-- RouterLink (navigation) -->
<a routerLink="/dashboard">Go to dashboard</a>

<!-- RouterOutlet (where routed components render – like @RenderBody()) -->
<router-outlet />
```

**Pipes** (like C# string format / Razor HTML helpers):
```html
{{ value | number }}          <!-- 1234.5 → "1,234.5" -->
{{ value | number:'1.1-1' }}  <!-- 1 decimal place: "3.0" -->
{{ date  | date:'mediumDate' }}  <!-- Mar 20, 2026 -->
{{ text  | uppercase }}
{{ text  | slice:0:50 }}
```

---

### 7.9 Parent → Child Data Flow: @Input / @Output

Think of it like MVC ViewComponents with callbacks.

```typescript
// CHILD component receives data from parent
@Component({ selector: 'app-kpi-cards', ... })
export class KpiCardsComponent {
  @Input() summary: FeedbackSummary | null = null;  // parent passes data IN
}

// Parent template uses it:
// <app-kpi-cards [summary]="summary"></app-kpi-cards>
//                ↑ [] = property binding, "summary" = parent's property
```

```typescript
// CHILD emits events UP to parent
@Component({ selector: 'app-filter-panel', ... })
export class FilterPanelComponent {
  @Input()  initialFilter: FeedbackFilter = {};
  @Output() filterChange = new EventEmitter<FeedbackFilter>();  // parent listens to this

  apply(): void {
    this.filterChange.emit({ ...this.filter });  // fires the event
  }
}

// Parent template:
// <app-filter-panel
//   [initialFilter]="filter"             ← pass data down
//   (filterChange)="onFilterChange($event)">  ← listen to events up
// </app-filter-panel>
//       ↑ () = event binding, $event = the emitted value
```

**Two-way binding `[()]` (banana in a box):**
```typescript
// In child: needs both @Input() x and @Output() xChange
@Input() groupBy: 'day' | 'week' | 'month' = 'day';
@Output() groupByChange = new EventEmitter<'day' | 'week' | 'month'>();

// Parent can then use:
// <app-trend-chart [(groupBy)]="trendGroupBy"></app-trend-chart>
// This is sugar for [groupBy]="trendGroupBy" (groupByChange)="trendGroupBy=$event"
```

---

### 7.10 forkJoin – Parallel HTTP Calls

Like `Task.WhenAll()` in C# — fires multiple HTTP requests simultaneously and waits for ALL to complete.

```typescript
import { forkJoin } from 'rxjs';

// Instead of sequential awaits:
// const summary = await svc.getSummary();
// const trends  = await svc.getTrends();      ← these are sequential, slow
// const list    = await svc.getAll();

// Do them in parallel:
forkJoin({
  summary: this.svc.getSummary(this.filter),
  trends:  this.svc.getTrends(from, to, 'day'),
  list:    this.svc.getAll(this.filter),
}).subscribe({
  next: ({ summary, trends, list }) => {  // destructured – all three arrive together
    this.summary    = summary;
    this.trendData  = trends;
    this.listResult = list;
    this.listLoading = false;
  },
  error: () => this.listLoading = false,
});
```

---

### 7.11 Chart.js / ng2-charts

**Installation:**
```bash
npm install chart.js ng2-charts
```

**Key concept:** You must register only the chart modules you use. This keeps the bundle small.

```typescript
import { BaseChartDirective } from 'ng2-charts'; // the Angular directive
import {
  Chart,
  LineElement, PointElement, LineController,
  CategoryScale, LinearScale, Tooltip, Legend, Filler
} from 'chart.js';

// Register once (at module/component level)
Chart.register(LineElement, PointElement, LineController, CategoryScale, LinearScale, Tooltip, Legend, Filler);

@Component({
  imports: [BaseChartDirective],  // must be in imports
  ...
})
export class TrendChartComponent implements OnChanges {
  @Input() trendData: TrendDataPoint[] = [];

  chartData: ChartData<'line'> = { labels: [], datasets: [] };

  chartOptions: ChartConfiguration<'line'>['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    scales: {
      y: { beginAtZero: true },
    },
  };

  ngOnChanges(): void {
    // Rebuild chart data whenever input changes
    this.chartData = {
      labels: this.trendData.map(d => d.date),
      datasets: [{
        label: 'Total',
        data: this.trendData.map(d => d.count),
        borderColor: '#6366f1',
        backgroundColor: 'rgba(99,102,241,.15)',
        fill: true,
        tension: 0.4,
      }],
    };
  }
}
```

```html
<!-- Template: fixed-height container required -->
<div style="position:relative; height:300px">
  <canvas
    baseChart
    [data]="chartData"
    [options]="chartOptions"
    type="line">
  </canvas>
</div>
```

**Chart types and their registrations:**

| Type | Required imports |
|---|---|
| `'line'` | `LineElement, PointElement, LineController, CategoryScale, LinearScale` |
| `'bar'` | `BarElement, BarController, CategoryScale, LinearScale` |
| `'doughnut'` | `DoughnutController, ArcElement` |
| All types | `Tooltip, Legend` (always include these) |

---

## 8. API ↔ Angular Contract Summary

| Endpoint | Method | Angular call | Returns |
|---|---|---|---|
| `/api/feedback` | GET | `svc.getAll(filter)` | `PagedResult<FeedbackItem>` |
| `/api/feedback/:id` | GET | `svc.getById(id)` | `FeedbackItem` |
| `/api/feedback` | POST | `svc.create(dto)` | `FeedbackItem` (201) |
| `/api/feedback/:id` | PUT | `svc.update(id, dto)` | `FeedbackItem` |
| `/api/feedback/:id` | DELETE | `svc.delete(id)` | `void` (204) |
| `/api/feedback/summary` | GET | `svc.getSummary(filter)` | `FeedbackSummary` |
| `/api/feedback/trends` | GET | `svc.getTrends(from,to,gb)` | `TrendDataPoint[]` |
| `/api/feedback/categories` | GET | `svc.getCategories()` | `string[]` |
| `/api/feedback/regions` | GET | `svc.getRegions()` | `string[]` |

**CORS:** The API must allow `http://localhost:4200`. Configured in `Program.cs` with `AddCors` + `UseCors`.  
**If you forget CORS**, the browser will block all requests with "Access-Control-Allow-Origin" error.

---

## 9. Common Mistakes & How to Avoid Them

### C# Backend
| Mistake | Fix |
|---|---|
| Forgetting `await` on async call | Return type becomes `Task<T>` instead of `T`; always await |
| Using `.Result` on a Task | Use `await` — `.Result` blocks and can deadlock |
| Not calling `await db.SaveChangesAsync()` | Changes never persist |
| Using `FindAsync` for read-only queries | Use `AsNoTracking().FirstOrDefaultAsync()` instead |
| Returning entity directly (not DTO) | Create a response DTO mapping |
| Missing `[ApiController]` attribute | Model validation and binding won't work |
| CORS before `MapControllers` | Order matters; `UseCors` must come first in pipeline |
| Wrong DI lifetime (Transient for DbContext) | Always `AddScoped` for DbContext |
| Not handling `null` return from service | Return `NotFound()` when service returns `null` |

### Angular
| Mistake | Fix |
|---|---|
| Forgetting `provideHttpClient()` | Add to `providers` in `app.config.ts` |
| Not importing `FormsModule` | `[(ngModel)]` won't work without it in component `imports: []` |
| Not subscribing to Observable | Nothing happens – must call `.subscribe()` |
| Forgetting `BaseChartDirective` in component imports | Chart renders nothing |
| Not registering Chart.js modules | "No scale registered with that id" error |
| Forgetting `[data]` binding on chart canvas | Chart won't update when data changes |
| Using `@Input` but no `[]` in parent template | Angular treats it as a static string |
| Missing `track` in `@for` | Warning/error: Angular requires unique track expression |

---

## 10. 60-Minute Build Checklist

### Minutes 0–10: Scaffold
```bash
dotnet new webapi -n FeedbackAPI --framework net8.0
dotnet add package Microsoft.EntityFrameworkCore.InMemory
dotnet add package Swashbuckle.AspNetCore
ng new feedback-dashboard --routing=true --style=scss --ssr=false --no-interactive
cd feedback-dashboard && npm install chart.js ng2-charts
```

### Minutes 10–20: Backend models + data layer
- [ ] `Models/CustomerFeedback.cs` – entity with enums
- [ ] `Data/FeedbackDbContext.cs` – DbContext with indexes in `OnModelCreating`
- [ ] `DTOs/FeedbackDtos.cs` – Create/Update/Response/Summary/Trend/Filter/Paged

### Minutes 20–35: Backend business logic + API
- [ ] `Services/IFeedbackService.cs` – interface
- [ ] `Services/FeedbackService.cs` – CRUD + GetSummaryAsync + GetTrendsAsync
- [ ] `Controllers/FeedbackController.cs` – 8 endpoints
- [ ] `Program.cs` – wire up DI, CORS, Swagger, seed data

### Minutes 35–45: Angular service + models
- [ ] `models/feedback.models.ts` – TypeScript interfaces
- [ ] `services/feedback.service.ts` – HttpClient wrapper
- [ ] `app.config.ts` – add `provideHttpClient(withFetch())`
- [ ] `app.routes.ts` – lazy-load Dashboard

### Minutes 45–58: Angular components
- [ ] `filter-panel` – `@Input initialFilter`, `@Output filterChange`
- [ ] `kpi-cards` – `@Input summary`, render numbers
- [ ] `trend-chart` – Chart.js line, `BaseChartDirective`, `ngOnChanges` rebuild
- [ ] `feedback-list` – table, pagination, resolve toggle
- [ ] **dashboard** (orchestrator) – `forkJoin`, connects all components

### Minutes 58–60: Run
```bash
dotnet run --project FeedbackAPI --urls "http://localhost:5000"
ng serve --port 4200 --open
```

---

## Key Numbers to Remember
- API base URL: `http://localhost:5000/api/feedback`
- Angular dev: `http://localhost:4200`
- Rating range: 1–5 (clamp with `Math.Clamp(v, 1, 5)`)
- Sentiment: ≥4 → Positive, =3 → Neutral, ≤2 → Negative
- Default page size: 20
- Trend groupBy options: `"day"`, `"week"`, `"month"`
- Seed records: 120 spread across 90 days

## C# Syntax Quick Reference (Modern .NET 8)
```csharp
// Primary constructor (inject in constructor signature)
public class MyService(IMyDep dep) { }

// Switch expression (no break needed)
var result = x switch { > 0 => "pos", 0 => "zero", _ => "neg" };

// Null-coalescing
var val = nullable ?? defaultValue;       // if null, use default
var result = obj?.Property ?? "fallback"; // safe navigate + fallback

// Null-conditional (won't throw if null)
string? trimmed = dto.Email?.Trim();

// Collection expression (C# 12)
IEnumerable<T> Data { get; set; } = [];   // empty collection init

// Pattern matching
if (entity is null) return null;
return item is null ? NotFound() : Ok(item);

// String interpolation
var key = $"feedback:{id}:{DateTime.UtcNow:yyyy-MM}";
```
