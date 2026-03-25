using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace FeedbackAPI.Services;

/// <summary>
/// Abstraction over Redis distributed cache.
/// Every method silently degrades when Redis is unavailable (connection refused / timeout)
/// so the API stays functional without a running Redis instance.
/// </summary>
public interface ICacheService
{
    /// <summary>Returns the cached value, or null on miss / Redis unavailable.</summary>
    Task<T?> GetAsync<T>(string key) where T : class;

    /// <summary>Stores value in Redis. Silently swallows errors if Redis is down.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class;

    /// <summary>Removes a key. Silently swallows errors if Redis is down.</summary>
    Task RemoveAsync(string key);
}

/// <summary>
/// Redis implementation of <see cref="ICacheService"/> backed by
/// <see cref="IDistributedCache"/> (registered with AddStackExchangeRedisCache in Program.cs).
///
/// Why IDistributedCache instead of raw IConnectionMultiplexer?
///   IDistributedCache is a .NET-standard abstraction. Swapping Redis → SQL Server cache or
///   in-memory cache in tests requires zero service class changes.
///
/// Default TTL = 60 seconds (suitable for dashboard aggregations).
/// </summary>
public class RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger)
    : ICacheService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    // ── GET ──────────────────────────────────────────────────────────────────
    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var json = await cache.GetStringAsync(key);
            if (json is null) return null;

            logger.LogDebug("Redis HIT  key={Key}", key);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            // Redis unreachable → log warning, return null (caller falls through to source)
            logger.LogWarning(ex, "Redis GET failed for '{Key}' – falling through to data source", key);
            return null;
        }
    }

    // ── SET ──────────────────────────────────────────────────────────────────
    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class
    {
        try
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl
            };
            var json = JsonSerializer.Serialize(value);
            await cache.SetStringAsync(key, json, options);
            logger.LogDebug("Redis SET  key={Key}  ttl={Ttl}s", key, (ttl ?? DefaultTtl).TotalSeconds);
        }
        catch (Exception ex)
        {
            // Cache write failure is non-fatal – next request will re-compute from SQL
            logger.LogWarning(ex, "Redis SET failed for '{Key}' – cache miss will occur on next call", key);
        }
    }

    // ── REMOVE ───────────────────────────────────────────────────────────────
    public async Task RemoveAsync(string key)
    {
        try
        {
            await cache.RemoveAsync(key);
            logger.LogDebug("Redis DEL  key={Key}", key);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis REMOVE failed for '{Key}'", key);
        }
    }
}
