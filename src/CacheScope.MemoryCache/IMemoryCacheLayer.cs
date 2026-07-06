namespace CacheScope.MemoryCache;

/// <summary>L2 — in-process application memory cache. The fastest layer; the nearest to the code.</summary>
public interface IMemoryCacheLayer
{
    bool TryGet<T>(string key, out T? value);
    void Set<T>(string key, T value, TimeSpan? ttl = null);
    void Remove(string key);

    /// <summary>Evicts everything — backs the "Clear Memory Cache" operation (Phase 5).</summary>
    void Clear();
}
