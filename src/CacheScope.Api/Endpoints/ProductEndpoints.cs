using System.Diagnostics;
using CacheScope.Api.Caching;
using CacheScope.Shared;
using CacheScope.Shared.Caching;
using CacheScope.Shared.Models;
using CacheScope.Shared.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace CacheScope.Api.Endpoints;

public static class ProductEndpoints
{
    // Edge/browser cache window. Runtime-configurable in Phase 5.
    private const int PublicMaxAgeSeconds = 30;

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products");

        group.MapGet("/", async (IProductCacheService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetIdsAsync(ct)))
            .WithName("ListProductIds");

        group.MapGet("/{id:int}", GetProduct).WithName("GetProduct");

        group.MapPut("/{id:int}", async (int id, ProductUpdate body, IProductCacheService svc, CancellationToken ct) =>
        {
            var updated = await svc.UpdateAsync(id, p =>
            {
                if (body.Price is { } price) p.Price = price;
                if (body.Stock is { } stock) p.Stock = stock;
            }, ct);

            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).WithName("UpdateProduct");

        return app;
    }

    private static async Task<IResult> GetProduct(
        int id,
        HttpContext ctx,
        IProductCacheService svc,
        IRequestCounter counter,
        IEnumerable<ITraceSink> sinks,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = await svc.GetAsync(id, ct);
        sw.Stop();

        var correlationId = ctx.Items[CorrelationConstants.CorrelationIdItemKey] as string ?? "unknown";
        var cfStatus = ctx.Request.Headers[CorrelationConstants.CfCacheStatusHeader].ToString();
        var statusCode = result.Found ? StatusCodes.Status200OK : StatusCodes.Status404NotFound;

        // Advertise the serving layer and make L0/L1 caching possible.
        ctx.Response.Headers[CorrelationConstants.ServedByHeader] = result.ServedBy.ToString();

        IResult response;
        if (!result.Found)
        {
            response = Results.NotFound();
        }
        else
        {
            var product = result.Value!;
            var etag = new EntityTagHeaderValue($"\"{product.Id}-v{product.Version}\"", isWeak: true);
            ctx.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
            {
                Public = true,
                MaxAge = TimeSpan.FromSeconds(PublicMaxAgeSeconds)
            };
            ctx.Response.GetTypedHeaders().ETag = etag;

            // Honour conditional requests — this is what lets the browser/edge revalidate cheaply.
            var inm = ctx.Request.GetTypedHeaders().IfNoneMatch;
            if (inm is not null && inm.Any(t => t.Compare(etag, useStrongComparison: false)))
            {
                statusCode = StatusCodes.Status304NotModified;
                response = Results.StatusCode(StatusCodes.Status304NotModified);
            }
            else
            {
                response = Results.Ok(product);
            }
        }

        await PublishTraceAsync(sinks, new RequestTrace
        {
            RequestId = counter.Next(),
            CorrelationId = correlationId,
            Timestamp = DateTimeOffset.UtcNow,
            Method = ctx.Request.Method,
            Path = ctx.Request.Path.Value ?? $"/api/products/{id}",
            ServedBy = result.ServedBy,
            Outcome = result.Outcome,
            ResponseTimeMs = sw.Elapsed.TotalMilliseconds,
            CfCacheStatus = string.IsNullOrEmpty(cfStatus) ? null : cfStatus,
            StatusCode = statusCode
        }, ct);

        return response;
    }

    private static async Task PublishTraceAsync(IEnumerable<ITraceSink> sinks, RequestTrace trace, CancellationToken ct)
    {
        foreach (var sink in sinks)
        {
            await sink.PublishAsync(trace, ct);
        }
    }

    public sealed record ProductUpdate(decimal? Price, int? Stock);
}
