using System.Diagnostics;
using CacheScope.Shared;
using CacheScope.Shared.Caching;
using CacheScope.Shared.Models;
using CacheScope.Shared.Tracing;

namespace CacheScope.Api.Caching;

public sealed class RequestExecutor(
    IProductCacheService cache,
    IRequestCounter counter,
    IRequestDetailStore details,
    IEnumerable<ITraceSink> sinks) : IRequestExecutor
{
    public async Task<RequestExecution> GetProductAsync(int id, string source, string? correlationId = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await cache.GetAsync(id, ct);
        sw.Stop();

        var (trace, detail) = Build("GET", $"/api/products/{id}", result.ServedBy, result.Outcome,
            sw.Elapsed.TotalMilliseconds, result.Found ? 200 : 404, source, correlationId, result.Segments);
        await PublishAsync(trace, detail, ct);
        return new RequestExecution(result, trace);
    }

    public async Task<RequestExecution> UpdateProductAsync(int id, decimal price, string source, string? correlationId = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var updated = await cache.UpdateAsync(id, p => p.Price = price, ct);
        sw.Stop();

        var result = updated is null
            ? CacheReadResult<Product>.NotFound()
            : CacheReadResult<Product>.Hit(updated, CacheLayer.Database);

        var (trace, detail) = Build("PUT", $"/api/products/{id}", CacheLayer.Database, result.Outcome,
            sw.Elapsed.TotalMilliseconds, updated is null ? 404 : 200, source, correlationId, result.Segments);
        await PublishAsync(trace, detail, ct);
        return new RequestExecution(result, trace);
    }

    private (RequestTrace, RequestDetail) Build(
        string method, string path, CacheLayer servedBy, CacheOutcome outcome, double elapsedMs,
        int statusCode, string source, string? correlationId, IReadOnlyList<LayerTiming> segments)
    {
        var id = counter.Next();
        var corr = correlationId ?? Guid.NewGuid().ToString("n");
        var traceId = Activity.Current?.TraceId.ToString();
        var now = DateTimeOffset.UtcNow;

        var trace = new RequestTrace
        {
            RequestId = id, CorrelationId = corr, Timestamp = now, Method = method, Path = path,
            ServedBy = servedBy, Outcome = outcome, ResponseTimeMs = elapsedMs, StatusCode = statusCode, Source = source
        };
        var detail = new RequestDetail
        {
            RequestId = id, CorrelationId = corr, TraceId = traceId, Timestamp = now, Method = method, Path = path,
            ServedBy = servedBy, Outcome = outcome, TotalMs = elapsedMs, StatusCode = statusCode, Source = source,
            Segments = segments
        };
        return (trace, detail);
    }

    private async Task PublishAsync(RequestTrace trace, RequestDetail detail, CancellationToken ct)
    {
        details.Record(detail);
        foreach (var sink in sinks)
        {
            await sink.PublishAsync(trace, ct);
        }
    }
}
