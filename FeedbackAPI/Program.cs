using Elastic.Clients.Elasticsearch;
using FeedbackAPI.Data;
using FeedbackAPI.DTOs;
using FeedbackAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// ── Controllers ──────────────────────────────────────────────────────────────
builder.Services.AddControllers();

// ── EF Core (In-Memory – swap to UseSqlServer / UseNpgsql in production) ─────
builder.Services.AddDbContext<FeedbackDbContext>(opt =>
    opt.UseInMemoryDatabase("FeedbackDb"));

// ── Application services ──────────────────────────────────────────────────────
builder.Services.AddScoped<IFeedbackService, FeedbackService>();

// ── Redis – distributed cache (IDistributedCache) ────────────────────────────
// Graceful degradation: if Redis is not running the API still works; cache is just skipped.
builder.Services.AddStackExchangeRedisCache(opt =>
{
    opt.Configuration  = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    opt.InstanceName   = "FeedbackAPI:";  // key prefix in Redis – avoids collisions between apps
});
builder.Services.AddScoped<ICacheService, RedisCacheService>();

// ── MongoDB – audit log store ─────────────────────────────────────────────────
// IMongoClient: Singleton (one connection pool per process)
// IMongoDatabase: Scoped (lightweight reference, safe to scope per request)
var mongoConnStr = builder.Configuration.GetConnectionString("MongoDB") ?? "mongodb://localhost:27017";
var mongoDbName  = builder.Configuration["MongoDB:Database"] ?? "FeedbackDB";
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnStr));
builder.Services.AddScoped<IMongoDatabase>(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDbName));
builder.Services.AddScoped<IAuditService, MongoAuditService>();

// ── Elasticsearch – full-text search index ────────────────────────────────────
// ElasticsearchClient: Singleton (thread-safe, manages its own connection pool)
var esUrl      = builder.Configuration.GetConnectionString("Elasticsearch") ?? "http://localhost:9200";
var esSettings = new ElasticsearchClientSettings(new Uri(esUrl))
    .DefaultIndex("feedback");            // used when no explicit index is specified
builder.Services.AddSingleton(new ElasticsearchClient(esSettings));
builder.Services.AddScoped<ISearchService, ElasticsearchService>();

// ── CORS – allow Angular dev server ──────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:4200",   // Angular default dev port
                "https://localhost:4200")
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// ── Swagger / OpenAPI ─────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "Customer Feedback API",
        Version     = "v1",
        Description = "Aggregates customer feedback and exposes trend/summary analytics."
    });
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
});

// ── Response caching ──────────────────────────────────────────────────────────
builder.Services.AddResponseCaching();

var app = builder.Build();

// ── Seed demo data ────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
    await SeedData.SeedAsync(db);

    // Seed Elasticsearch from SQL so full-text search works immediately
    // In production this would be a one-time migration job, not per-startup logic
    try
    {
        var searchSvc = scope.ServiceProvider.GetRequiredService<ISearchService>();
        var allItems  = await db.Feedbacks.AsNoTracking().ToListAsync();
        var dtos      = allItems.Select(f => new FeedbackResponseDto
        {
            Id            = f.Id,
            CustomerName  = f.CustomerName,
            CustomerEmail = f.CustomerEmail,
            Category      = f.Category.ToString(),
            Sentiment     = f.Sentiment.ToString(),
            Rating        = f.Rating,
            Comment       = f.Comment,
            CreatedAt     = f.CreatedAt,
            ProductId     = f.ProductId,
            Region        = f.Region,
            IsResolved    = f.IsResolved
        });
        await searchSvc.BulkIndexAsync(dtos);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Elasticsearch seeding skipped (service may not be running): {Msg}", ex.Message);
    }
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Customer Feedback API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors("AllowAngular");
app.UseResponseCaching();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

