namespace CacheScope.Shared.Traffic;

/// <summary>
/// Cache-administration operations the traffic generator needs to set up patterns
/// (cold start flushes, warm cache, stampede expiry, L4 warm-up).
/// Implemented in the Api layer, which has access to all cache layers.
/// </summary>
public interface ITrafficSupport
{
    /// <summary>The set of product ids traffic can target.</summary>
    Task<IReadOnlyList<int>> GetKeyUniverseAsync(CancellationToken ct = default);

    /// <summary>Touch L4 once before a run so first-query overhead doesn't skew latency.</summary>
    Task ResumeDatabaseAsync(CancellationToken ct = default);

    void ClearMemory();
    Task ClearDistributedAsync(CancellationToken ct = default);

    /// <summary>Expire a single product across L2 + L3 (used by the stampede pattern).</summary>
    Task ExpireAsync(int productId, CancellationToken ct = default);
}
