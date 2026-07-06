using CacheScope.Database;
using CacheScope.MemoryCache;
using CacheScope.RedisCache;
using CacheScope.Shared.Operations;

namespace CacheScope.Api.Caching;

public sealed class CacheOperations(
    IProductStore store,
    IMemoryCacheLayer memory,
    IRedisCacheLayer redis,
    ICloudflarePurger cloudflare) : ICacheOperations
{
    public CacheOpResult ClearMemory()
    {
        memory.Clear();
        return new CacheOpResult("clear-memory", "L2 memory cache cleared.");
    }

    public async Task<CacheOpResult> ClearRedisAsync(CancellationToken ct = default)
    {
        await redis.FlushAsync(ct);
        return new CacheOpResult("clear-redis", "L3 Redis cache flushed (CacheScope keys).");
    }

    public async Task<CacheOpResult> WarmMemoryAsync(CancellationToken ct = default)
    {
        var ids = await store.GetAllIdsAsync(ct);
        long n = 0;
        foreach (var id in ids)
        {
            var product = await store.GetByIdAsync(id, ct);
            if (product is not null) { memory.Set(ProductKeys.For(id), product); n++; }
        }
        return new CacheOpResult("warm-memory", $"Warmed {n} products into L2 memory.", n);
    }

    public async Task<CacheOpResult> WarmRedisAsync(CancellationToken ct = default)
    {
        var ids = await store.GetAllIdsAsync(ct);
        long n = 0;
        foreach (var id in ids)
        {
            var product = await store.GetByIdAsync(id, ct);
            if (product is not null) { await redis.SetAsync(ProductKeys.For(id), product, ct: ct); n++; }
        }
        return new CacheOpResult("warm-redis", $"Warmed {n} products into L3 Redis.", n);
    }

    public async Task<CacheOpResult> ExpireProductAsync(int id, CancellationToken ct = default)
    {
        var key = ProductKeys.For(id);
        memory.Remove(key);
        await redis.ExpireNowAsync(key, ct);
        return new CacheOpResult("expire", $"Expired product {id} from L2 + L3.", 1);
    }

    public async Task<CacheOpResult> InvalidateProductAsync(int id, CancellationToken ct = default)
    {
        var key = ProductKeys.For(id);
        memory.Remove(key);
        await redis.RemoveAsync(key, ct);
        return new CacheOpResult("invalidate", $"Invalidated product {id} across all cache layers.", 1);
    }

    public async Task<CacheOpResult> FlushAllAsync(CancellationToken ct = default)
    {
        memory.Clear();
        await redis.FlushAsync(ct);
        return new CacheOpResult("flush", "Flushed L2 memory + L3 Redis.");
    }

    public async Task<CacheOpResult> PurgeCloudflareAsync(CancellationToken ct = default)
    {
        var result = await cloudflare.PurgeAllAsync(ct);
        return new CacheOpResult("purge-cloudflare", result.Message);
    }
}
