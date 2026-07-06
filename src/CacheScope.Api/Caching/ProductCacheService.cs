using System.Diagnostics;
using CacheScope.Database;
using CacheScope.MemoryCache;
using CacheScope.RedisCache;
using CacheScope.Shared;
using CacheScope.Shared.Analytics;
using CacheScope.Shared.Caching;
using CacheScope.Shared.Models;
using CacheScope.Shared.Tracing;
using Microsoft.Extensions.Logging;

namespace CacheScope.Api.Caching;

public sealed class ProductCacheService(
    IMemoryCacheLayer memory,
    IRedisCacheLayer redis,
    IProductStore store,
    IDatabaseMetrics dbMetrics,
    ICachePolicy policy,
    IWriteBehindQueue writeBehind,
    RefreshAheadScheduler refreshAhead,
    SingleFlight singleFlight,
    ActivitySource activitySource,
    ILogger<ProductCacheService> logger) : IProductCacheService
{
    private static string Key(int id) => ProductKeys.For(id);

    public async Task<CacheReadResult<Product>> GetAsync(int id, CancellationToken ct = default)
    {
        var key = Key(id);
        var refreshAheadOn = policy.WriteStrategy == WriteStrategy.RefreshAhead;
        var segments = new List<LayerTiming>(3);

        // L2 — memory (each layer is a span so the distributed trace mirrors the pipeline)
        var sw = Stopwatch.GetTimestamp();
        Product? fromMemory;
        using (activitySource.StartActivity("cache.memory"))
        {
            memory.TryGet(key, out fromMemory);
        }
        var memMs = Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
        if (fromMemory is not null)
        {
            segments.Add(new LayerTiming { Layer = CacheLayer.Memory, Ms = memMs, Outcome = CacheOutcome.Hit });
            dbMetrics.RecordPrevented();
            if (refreshAheadOn) refreshAhead.MaybeSchedule(id, policy.MemoryTtl);
            return Hit(fromMemory, CacheLayer.Memory, segments);
        }
        segments.Add(new LayerTiming { Layer = CacheLayer.Memory, Ms = memMs, Outcome = CacheOutcome.Miss });

        // L3 — Redis (tolerate an unavailable Redis by falling through to the DB)
        sw = Stopwatch.GetTimestamp();
        Product? fromRedis = null;
        using (activitySource.StartActivity("cache.redis"))
        {
            try { fromRedis = await redis.GetAsync<Product>(key, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Redis read failed for {Key}; falling through", key); }
        }
        var redisMs = Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
        if (fromRedis is not null)
        {
            segments.Add(new LayerTiming { Layer = CacheLayer.Redis, Ms = redisMs, Outcome = CacheOutcome.Hit });
            memory.Set(key, fromRedis);            // repopulate L2
            if (refreshAheadOn) refreshAhead.RecordLoad(id);
            dbMetrics.RecordPrevented();
            return Hit(fromRedis, CacheLayer.Redis, segments);
        }
        segments.Add(new LayerTiming { Layer = CacheLayer.Redis, Ms = redisMs, Outcome = CacheOutcome.Miss });

        // L4 — database. With stampede protection on, concurrent misses coalesce into one load.
        sw = Stopwatch.GetTimestamp();
        Product? fromDb;
        using (activitySource.StartActivity("cache.database"))
        {
            fromDb = policy.StampedeProtection
                ? await singleFlight.RunAsync(key, () => LoadAndPopulateAsync(id, key, refreshAheadOn, ct))
                : await LoadAndPopulateAsync(id, key, refreshAheadOn, ct);
        }
        var dbMs = Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
        segments.Add(new LayerTiming
        {
            Layer = CacheLayer.Database,
            Ms = dbMs,
            Outcome = fromDb is null ? CacheOutcome.Miss : CacheOutcome.Hit
        });

        return fromDb is null
            ? (CacheReadResult<Product>.NotFound() with { Segments = segments })
            : Hit(fromDb, CacheLayer.Database, segments);
    }

    private static CacheReadResult<Product> Hit(Product value, CacheLayer layer, List<LayerTiming> segments) =>
        CacheReadResult<Product>.Hit(value, layer) with { Segments = segments };

    private async Task<Product?> LoadAndPopulateAsync(int id, string key, bool refreshAheadOn, CancellationToken ct)
    {
        var fromDb = await store.GetByIdAsync(id, ct);   // the query is recorded inside the store
        if (fromDb is null) return null;

        await TrySetRedisAsync(key, fromDb, ct);          // repopulate L3
        memory.Set(key, fromDb);                          // repopulate L2
        if (refreshAheadOn) refreshAhead.RecordLoad(id);
        return fromDb;
    }

    public Task<IReadOnlyList<int>> GetIdsAsync(CancellationToken ct = default) => store.GetAllIdsAsync(ct);

    public Task<Product?> UpdateAsync(int id, Action<Product> mutate, CancellationToken ct = default) =>
        policy.WriteStrategy switch
        {
            WriteStrategy.WriteThrough or WriteStrategy.RefreshAhead => WriteThroughAsync(id, mutate, ct),
            WriteStrategy.WriteBehind => WriteBehindAsync(id, mutate, ct),
            _ => CacheAsideAsync(id, mutate, ct)
        };

    // Write to DB, then drop cached copies; next read repopulates.
    private async Task<Product?> CacheAsideAsync(int id, Action<Product> mutate, CancellationToken ct)
    {
        var updated = await store.UpdateAsync(id, mutate, ct);
        if (updated is null) return null;

        var key = Key(id);
        memory.Remove(key);
        await TryRemoveRedisAsync(key, ct);
        return updated;
    }

    // Write to DB, then write the fresh value straight into the caches.
    private async Task<Product?> WriteThroughAsync(int id, Action<Product> mutate, CancellationToken ct)
    {
        var updated = await store.UpdateAsync(id, mutate, ct);
        if (updated is null) return null;

        var key = Key(id);
        await TrySetRedisAsync(key, updated, ct);
        memory.Set(key, updated);
        if (policy.WriteStrategy == WriteStrategy.RefreshAhead) refreshAhead.RecordLoad(id);
        return updated;
    }

    // Update caches immediately (optimistic), persist to DB asynchronously.
    private async Task<Product?> WriteBehindAsync(int id, Action<Product> mutate, CancellationToken ct)
    {
        var key = Key(id);

        // Get the current state from the nearest layer without a DB write.
        Product? current = null;
        if (memory.TryGet<Product>(key, out var m) && m is not null) current = m;
        if (current is null)
        {
            try { current = await redis.GetAsync<Product>(key, ct); } catch { /* fall through */ }
        }
        current ??= await store.GetByIdAsync(id, ct);
        if (current is null) return null;

        mutate(current);
        current.Version++;
        current.UpdatedAt = DateTimeOffset.UtcNow;

        memory.Set(key, current);
        await TrySetRedisAsync(key, current, ct);
        writeBehind.Enqueue(new WriteBehindItem(id, current.Price)); // persist later
        return current;
    }

    private async Task TrySetRedisAsync(string key, Product product, CancellationToken ct)
    {
        try { await redis.SetAsync(key, product, ct: ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Redis write failed for {Key}", key); }
    }

    private async Task TryRemoveRedisAsync(string key, CancellationToken ct)
    {
        try { await redis.RemoveAsync(key, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Redis invalidation failed for {Key}", key); }
    }
}
