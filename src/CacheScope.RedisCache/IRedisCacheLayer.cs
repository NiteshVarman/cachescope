namespace CacheScope.RedisCache;

/// <summary>L3 — distributed cache shared across all app instances.</summary>
public interface IRedisCacheLayer
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Expire a key immediately — backs the "Expire Keys" operation (Phase 5/6).</summary>
    Task<bool> ExpireNowAsync(string key, CancellationToken ct = default);

    /// <summary>Flush all CacheScope keys — backs "Clear Redis" (Phase 5).</summary>
    Task FlushAsync(CancellationToken ct = default);

    bool IsConnected { get; }
}
