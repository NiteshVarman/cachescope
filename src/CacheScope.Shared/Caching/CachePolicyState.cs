namespace CacheScope.Shared.Caching;

/// <summary>Thread-safe implementation of <see cref="ICachePolicy"/>.</summary>
public sealed class CachePolicyState : ICachePolicy
{
    private readonly Lock _gate = new();
    private TimeSpan _memoryTtl = TimeSpan.FromSeconds(30);
    private TimeSpan _redisTtl = TimeSpan.FromMinutes(5);
    private ExpirationMode _memoryExpiration = ExpirationMode.Absolute;
    private WriteStrategy _writeStrategy = WriteStrategy.CacheAside;

    public TimeSpan MemoryTtl { get { lock (_gate) return _memoryTtl; } }
    public TimeSpan RedisTtl { get { lock (_gate) return _redisTtl; } }
    public ExpirationMode MemoryExpiration { get { lock (_gate) return _memoryExpiration; } }
    public WriteStrategy WriteStrategy { get { lock (_gate) return _writeStrategy; } }

    public CachePolicySnapshot Snapshot()
    {
        lock (_gate)
        {
            return new CachePolicySnapshot
            {
                MemoryTtlSeconds = _memoryTtl.TotalSeconds,
                RedisTtlSeconds = _redisTtl.TotalSeconds,
                MemoryExpiration = _memoryExpiration,
                WriteStrategy = _writeStrategy
            };
        }
    }

    public CachePolicySnapshot Apply(CachePolicyUpdate update)
    {
        lock (_gate)
        {
            if (update.MemoryTtlSeconds is { } m and > 0) _memoryTtl = TimeSpan.FromSeconds(m);
            if (update.RedisTtlSeconds is { } r and > 0) _redisTtl = TimeSpan.FromSeconds(r);
            if (update.MemoryExpiration is { } exp) _memoryExpiration = exp;
            if (update.WriteStrategy is { } ws) _writeStrategy = ws;
        }
        return Snapshot();
    }
}
