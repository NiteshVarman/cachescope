using CacheScope.Database;
using CacheScope.MemoryCache;
using CacheScope.RedisCache;
using CacheScope.Shared;
using CacheScope.Shared.Analytics;
using CacheScope.Shared.Caching;
using CacheScope.Shared.Models;
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
    ILogger<ProductCacheService> logger) : IProductCacheService
{
    private static string Key(int id) => ProductKeys.For(id);

    public async Task<CacheReadResult<Product>> GetAsync(int id, CancellationToken ct = default)
    {
        var key = Key(id);
        var refreshAheadOn = policy.WriteStrategy == WriteStrategy.RefreshAhead;

        // L2 — memory
        if (memory.TryGet<Product>(key, out var fromMemory) && fromMemory is not null)
        {
            dbMetrics.RecordPrevented();
            if (refreshAheadOn) refreshAhead.MaybeSchedule(id, policy.MemoryTtl);
            return CacheReadResult<Product>.Hit(fromMemory, CacheLayer.Memory);
        }

        // L3 — Redis (tolerate an unavailable Redis by falling through to the DB)
        Product? fromRedis = null;
        try
        {
            fromRedis = await redis.GetAsync<Product>(key, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis read failed for {Key}; falling through to database", key);
        }

        if (fromRedis is not null)
        {
            memory.Set(key, fromRedis);            // repopulate L2
            if (refreshAheadOn) refreshAhead.RecordLoad(id);
            dbMetrics.RecordPrevented();
            return CacheReadResult<Product>.Hit(fromRedis, CacheLayer.Redis);
        }

        // L4 — database (source of truth). The query is recorded inside the store.
        var fromDb = await store.GetByIdAsync(id, ct);
        if (fromDb is null)
        {
            return CacheReadResult<Product>.NotFound();
        }

        await TrySetRedisAsync(key, fromDb, ct);   // repopulate L3
        memory.Set(key, fromDb);                    // repopulate L2
        if (refreshAheadOn) refreshAhead.RecordLoad(id);
        return CacheReadResult<Product>.Hit(fromDb, CacheLayer.Database);
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
