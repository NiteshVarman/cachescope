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
    ILogger<ProductCacheService> logger) : IProductCacheService
{
    private static string Key(int id) => ProductKeys.For(id);

    public async Task<CacheReadResult<Product>> GetAsync(int id, CancellationToken ct = default)
    {
        var key = Key(id);

        // L2 — memory
        if (memory.TryGet<Product>(key, out var fromMemory) && fromMemory is not null)
        {
            dbMetrics.RecordPrevented();
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
        return CacheReadResult<Product>.Hit(fromDb, CacheLayer.Database);
    }

    public Task<IReadOnlyList<int>> GetIdsAsync(CancellationToken ct = default) => store.GetAllIdsAsync(ct);

    public async Task<Product?> UpdateAsync(int id, Action<Product> mutate, CancellationToken ct = default)
    {
        var updated = await store.UpdateAsync(id, mutate, ct);
        if (updated is null)
        {
            return null;
        }

        // Cache-aside invalidation: drop stale copies so the next read re-populates.
        var key = Key(id);
        memory.Remove(key);
        try
        {
            await redis.RemoveAsync(key, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis invalidation failed for {Key}", key);
        }

        return updated;
    }

    private async Task TrySetRedisAsync(string key, Product product, CancellationToken ct)
    {
        try
        {
            await redis.SetAsync(key, product, ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis write failed for {Key}", key);
        }
    }
}
