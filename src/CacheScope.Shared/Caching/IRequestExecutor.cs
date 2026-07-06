using CacheScope.Shared.Models;
using CacheScope.Shared.Tracing;

namespace CacheScope.Shared.Caching;

/// <summary>
/// Runs a single request through the cache pipeline, builds its <see cref="RequestTrace"/>,
/// and publishes it to all trace sinks. Shared by the traffic generator so generated load
/// travels the exact same path (and appears in the same stream) as real requests.
/// </summary>
public interface IRequestExecutor
{
    Task<RequestExecution> GetProductAsync(int id, string source, CancellationToken ct = default);
    Task<RequestExecution> UpdateProductAsync(int id, decimal price, string source, CancellationToken ct = default);
}

public sealed record RequestExecution(CacheReadResult<Product> Result, RequestTrace Trace);
