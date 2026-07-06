namespace CacheScope.Shared.Operations;

public sealed record CacheOpResult(string Operation, string Message, long Affected = 0);

/// <summary>The runtime cache-administration operations exposed by the Cache Operations panel.</summary>
public interface ICacheOperations
{
    CacheOpResult ClearMemory();
    Task<CacheOpResult> ClearRedisAsync(CancellationToken ct = default);
    Task<CacheOpResult> WarmMemoryAsync(CancellationToken ct = default);
    Task<CacheOpResult> WarmRedisAsync(CancellationToken ct = default);
    Task<CacheOpResult> ExpireProductAsync(int id, CancellationToken ct = default);
    Task<CacheOpResult> InvalidateProductAsync(int id, CancellationToken ct = default);
    Task<CacheOpResult> FlushAllAsync(CancellationToken ct = default);
    Task<CacheOpResult> PurgeCloudflareAsync(CancellationToken ct = default);
}
