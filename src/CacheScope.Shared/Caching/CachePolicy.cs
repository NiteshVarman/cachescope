namespace CacheScope.Shared.Caching;

/// <summary>How a written value is propagated to the cache layers relative to the database.</summary>
public enum WriteStrategy
{
    /// <summary>Write to DB, invalidate caches; next read repopulates. (default)</summary>
    CacheAside,

    /// <summary>Write to DB, then immediately write the fresh value into the caches.</summary>
    WriteThrough,

    /// <summary>Write to caches immediately; persist to DB asynchronously in the background.</summary>
    WriteBehind,

    /// <summary>Write-through on writes; on reads, proactively refresh entries nearing expiry.</summary>
    RefreshAhead
}

/// <summary>Memory-layer expiration behaviour.</summary>
public enum ExpirationMode
{
    Absolute,
    Sliding
}

/// <summary>Serializable view of the current cache policy.</summary>
public sealed record CachePolicySnapshot
{
    public double MemoryTtlSeconds { get; init; }
    public double RedisTtlSeconds { get; init; }
    public ExpirationMode MemoryExpiration { get; init; }
    public WriteStrategy WriteStrategy { get; init; }
}

/// <summary>A partial update to the cache policy; only non-null fields are applied.</summary>
public sealed record CachePolicyUpdate
{
    public double? MemoryTtlSeconds { get; init; }
    public double? RedisTtlSeconds { get; init; }
    public ExpirationMode? MemoryExpiration { get; init; }
    public WriteStrategy? WriteStrategy { get; init; }
}

/// <summary>
/// Runtime-mutable cache policy. The cache layers and orchestrator read from this instead
/// of static options, so TTLs, expiration mode and write strategy can be changed live.
/// </summary>
public interface ICachePolicy
{
    TimeSpan MemoryTtl { get; }
    TimeSpan RedisTtl { get; }
    ExpirationMode MemoryExpiration { get; }
    WriteStrategy WriteStrategy { get; }

    CachePolicySnapshot Snapshot();
    CachePolicySnapshot Apply(CachePolicyUpdate update);
}
