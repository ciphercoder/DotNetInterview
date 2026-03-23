# Cloud Deployment & Performance Optimization Strategy

## Architecture Overview

```
  Browser (Angular SPA)
        │  HTTPS
        ▼
  CDN (Azure Front Door / CloudFront)
        │                     │
        ▼                     ▼
  Angular static files   API Gateway / Load Balancer
  (Blob Storage / S3)         │
                              ▼
                    App Service / ECS (FeedbackAPI)
                              │
                              ▼
                     Azure SQL / RDS PostgreSQL
                              │
                              ▼
                      Azure Cache for Redis
```

---

## 1. Cloud Deployment – Azure (recommended)

### Frontend (Angular SPA)
| Concern | Solution |
|---|---|
| Hosting | Azure Static Web Apps (free tier supports CI/CD from GitHub) |
| CDN | Azure Front Door Standard – global edge caching of `dist/` assets |
| Custom domain + TLS | Managed certificate via Front Door |
| Environment config | `environment.prod.ts` injected at build time via CI variable |

```yaml
# azure-static-webapp.yml (GitHub Actions snippet)
- name: Deploy Angular
  uses: Azure/static-web-apps-deploy@v1
  with:
    app_location: "feedback-dashboard"
    output_location: "dist/feedback-dashboard/browser"
```

### Backend (FeedbackAPI .NET 8)
| Concern | Solution |
|---|---|
| Compute | Azure App Service (Linux, B2 for dev → P2v3 for prod) |
| Container option | Azure Container Apps (scale to zero, KEDA-based autoscale) |
| Database | Azure SQL Serverless (dev) → Azure SQL Business Critical (prod) |
| Secrets | Azure Key Vault + Managed Identity (no credentials in config) |
| Health checks | `app.MapHealthChecks("/health")` wired to App Service health probe |

### Infrastructure as Code
Use **Bicep** or **Terraform** to declare all resources for reproducible environments.

---

## 2. Database (Production)

Replace the In-Memory provider in `Program.cs`:

```csharp
// appsettings.Production.json
{
  "ConnectionStrings": {
    "FeedbackDb": "Server=...;Database=FeedbackDB;..."
  }
}

// Program.cs (production branch)
builder.Services.AddDbContext<FeedbackDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("FeedbackDb"),
        sql => sql.EnableRetryOnFailure(maxRetryCount: 5)));
```

**Schema migrations:** `dotnet ef migrations add Init && dotnet ef database update`

---

## 3. Performance Optimization

### API / Backend

| Technique | Implementation |
|---|---|
| **Response caching** | `[ResponseCache(Duration = 60)]` on `/summary` and `/trends` endpoints |
| **Distributed cache** | `IDistributedCache` (Redis) for expensive aggregation queries |
| **Async all the way** | All EF Core calls use `async/await` (already done) |
| **Compiled queries** | `EF.CompileAsyncQuery(...)` for hot read paths |
| **Indexes** | `CreatedAt`, `Category`, `Sentiment` indexes on `CustomerFeedback` (already in `OnModelCreating`) |
| **Pagination** | All list endpoints are paginated (already done) |
| **Output caching** (.NET 8+) | `builder.Services.AddOutputCache()` with tag-based invalidation |
| **Compression** | `builder.Services.AddResponseCompression(opt => opt.EnableForHttps = true)` |
| **Rate limiting** | `builder.Services.AddRateLimiter(...)` – fixed window per IP |

### Angular / Frontend

| Technique | Implementation |
|---|---|
| **Lazy loading** | Dashboard loaded via `loadComponent` (already done) |
| **OnPush change detection** | Add `changeDetection: ChangeDetectionStrategy.OnPush` to all components |
| **Virtual scrolling** | `@angular/cdk/scrolling` `<cdk-virtual-scroll-viewport>` for the feedback table |
| **Debounced filter** | Wrap filter changes in `debounceTime(300)` via `rxjs` |
| **HTTP caching** | Use HTTP `ETag`/`Last-Modified` headers returned by the API |
| **Bundle optimization** | Already tree-shaken; only register used Chart.js modules (already done) |
| **Asset optimization** | Brotli/gzip via CDN, `budgets` in `angular.json` |
| **Preloading strategy** | `PreloadAllModules` or `QuicklinkStrategy` for route preloading |

---

## 4. Scalability

### Horizontal scaling
- **Stateless API** – no in-process session state; any instance can handle any request.
- **Azure Container Apps** autoscales on HTTP queue depth (KEDA HTTP scaler).
- **Read replicas** – route `/summary` and `/trends` queries to a read replica.

### Caching tier

```csharp
// FeedbackService.cs – cached summary example
private const string SummaryCacheKey = "feedback:summary";

public async Task<FeedbackSummaryDto> GetSummaryAsync(FeedbackFilterParams filter)
{
    var key = $"{SummaryCacheKey}:{JsonSerializer.Serialize(filter)}";
    var cached = await _cache.GetStringAsync(key);
    if (cached != null)
        return JsonSerializer.Deserialize<FeedbackSummaryDto>(cached)!;

    var result = await ComputeSummaryAsync(filter);
    await _cache.SetStringAsync(key, JsonSerializer.Serialize(result),
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
    return result;
}
```

### Background processing (future)
- Use **Azure Service Bus** + **IHostedService** worker to process incoming feedback asynchronously.
- Sentiment analysis via **Azure AI Language** or a lightweight ML.NET model.

---

## 5. Reliability & Observability

| Concern | Tool |
|---|---|
| Structured logging | `Serilog` → Azure Application Insights |
| Distributed tracing | `OpenTelemetry` with `ActivitySource` |
| Health checks | `/health` (liveness) + `/health/ready` (readiness) |
| Alerting | Azure Monitor alerts on p95 latency > 500 ms or error rate > 1% |
| Circuit breaker | `Microsoft.Extensions.Http.Resilience` (Polly v8) |

---

## 6. Security Checklist

- [x] CORS locked to known origins (Angular dev + prod domains)
- [x] Input validation via model validation attributes
- [x] Parameterized queries via EF Core (no raw SQL injection risk)
- [ ] Add **authentication**: Azure AD B2C or JWT Bearer tokens
- [ ] Enable **HTTPS redirect** and HSTS in production
- [ ] Store connection strings in **Azure Key Vault** (not `appsettings`)
- [ ] Enable **Azure Defender for SQL** for threat detection
- [ ] Rotate secrets via **Key Vault rotation policies**

---

## 7. CI/CD Pipeline (GitHub Actions)

```yaml
name: Build & Deploy

on:
  push:
    branches: [main]

jobs:
  backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.x' }
      - run: dotnet build FeedbackAPI --configuration Release
      - run: dotnet test FeedbackAPI
      - run: dotnet publish FeedbackAPI -c Release -o publish
      - uses: azure/webapps-deploy@v3
        with:
          app-name: feedback-api-prod
          publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}
          package: publish

  frontend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '22' }
      - run: npm ci
        working-directory: feedback-dashboard
      - run: ng build --configuration production
        working-directory: feedback-dashboard
      - uses: Azure/static-web-apps-deploy@v1
        with:
          azure_static_web_apps_api_token: ${{ secrets.AZURE_STATIC_WEB_APPS_TOKEN }}
          app_location: "feedback-dashboard"
          output_location: "dist/feedback-dashboard/browser"
```
