using System.Collections.Concurrent;
using System.Diagnostics;
using CacheScope.Database;
using CacheScope.MemoryCache;
using CacheScope.RedisCache;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CacheScope.Api.Caching;

/// <summary>
/// Backs the Refresh-Ahead strategy. Singleton (so it outlives request scopes): tracks when
/// each key was last loaded and, when a cache hit lands on an entry past ~70% of its TTL,
/// proactively reloads it from the database in the background so it never expires under load.
/// </summary>
public sealed class RefreshAheadScheduler(
    IServiceScopeFactory scopeFactory,
    IMemoryCacheLayer memory,
    IRedisCacheLayer redis,
    ILogger<RefreshAheadScheduler> logger)
{
    private const double RefreshThreshold = 0.70;
    private readonly ConcurrentDictionary<int, long> _loadedAt = new();
    private readonly ConcurrentDictionary<int, byte> _inflight = new();

    public void RecordLoad(int id) => _loadedAt[id] = Stopwatch.GetTimestamp();

    public void MaybeSchedule(int id, TimeSpan ttl)
    {
        if (!_loadedAt.TryGetValue(id, out var loadedAt)) return;
        var age = Stopwatch.GetElapsedTime(loadedAt);
        if (age < ttl * RefreshThreshold) return;
        if (!_inflight.TryAdd(id, 0)) return;

        _ = RefreshAsync(id);
    }

    private async Task RefreshAsync(int id)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IProductStore>();
            var product = await store.GetByIdAsync(id);
            if (product is not null)
            {
                var key = ProductKeys.For(id);
                await redis.SetAsync(key, product);
                memory.Set(key, product);
                RecordLoad(id);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Refresh-ahead for product {Id} failed", id);
        }
        finally
        {
            _inflight.TryRemove(id, out _);
        }
    }
}
