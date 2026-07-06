using System.Diagnostics;
using CacheScope.Shared;
using CacheScope.Shared.Caching;
using CacheScope.Shared.Tracing;

namespace CacheScope.Api.Caching;

public sealed class RequestExecutor(
    IProductCacheService cache,
    IRequestCounter counter,
    IEnumerable<ITraceSink> sinks) : IRequestExecutor
{
    public async Task<RequestExecution> GetProductAsync(int id, string source, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await cache.GetAsync(id, ct);
        sw.Stop();

        var trace = BuildTrace("GET", $"/api/products/{id}", result.ServedBy, result.Outcome,
            sw.Elapsed.TotalMilliseconds, result.Found ? 200 : 404, source);
        await PublishAsync(trace, ct);
        return new RequestExecution(result, trace);
    }

    public async Task<RequestExecution> UpdateProductAsync(int id, decimal price, string source, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var updated = await cache.UpdateAsync(id, p => p.Price = price, ct);
        sw.Stop();

        // A write always lands at the database and invalidates the caches.
        var result = updated is null
            ? CacheReadResult<Shared.Models.Product>.NotFound()
            : CacheReadResult<Shared.Models.Product>.Hit(updated, CacheLayer.Database);

        var trace = BuildTrace("PUT", $"/api/products/{id}", CacheLayer.Database, result.Outcome,
            sw.Elapsed.TotalMilliseconds, updated is null ? 404 : 200, source);
        await PublishAsync(trace, ct);
        return new RequestExecution(result, trace);
    }

    private RequestTrace BuildTrace(string method, string path, CacheLayer servedBy, CacheOutcome outcome,
        double elapsedMs, int statusCode, string source) => new()
    {
        RequestId = counter.Next(),
        CorrelationId = Guid.NewGuid().ToString("n"),
        Timestamp = DateTimeOffset.UtcNow,
        Method = method,
        Path = path,
        ServedBy = servedBy,
        Outcome = outcome,
        ResponseTimeMs = elapsedMs,
        StatusCode = statusCode,
        Source = source
    };

    private async Task PublishAsync(RequestTrace trace, CancellationToken ct)
    {
        foreach (var sink in sinks)
        {
            await sink.PublishAsync(trace, ct);
        }
    }
}
