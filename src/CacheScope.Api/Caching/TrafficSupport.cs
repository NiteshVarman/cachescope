using CacheScope.Database;
using CacheScope.MemoryCache;
using CacheScope.RedisCache;
using CacheScope.Shared.Traffic;

namespace CacheScope.Api.Caching;

public sealed class TrafficSupport(
    IProductStore store,
    IMemoryCacheLayer memory,
    IRedisCacheLayer redis) : ITrafficSupport
{
    public Task<IReadOnlyList<int>> GetKeyUniverseAsync(CancellationToken ct = default) =>
        store.GetAllIdsAsync(ct);

    // Warms the L4 path before a run. With embedded SQLite there is no cold start to resume, but
    // this still touches the DB once so the first measured query isn't skewed by JIT/first-touch.
    public Task ResumeDatabaseAsync(CancellationToken ct = default) => store.GetAllIdsAsync(ct);

    public void ClearMemory() => memory.Clear();

    public Task ClearDistributedAsync(CancellationToken ct = default) => redis.FlushAsync(ct);

    public async Task ExpireAsync(int productId, CancellationToken ct = default)
    {
        var key = ProductKeys.For(productId);
        memory.Remove(key);
        await redis.ExpireNowAsync(key, ct);
    }
}
