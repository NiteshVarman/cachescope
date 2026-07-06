using CacheScope.Api.Caching;
using CacheScope.Shared.Caching;
using CacheScope.Shared.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace CacheScope.Api.Endpoints;

public static class ProductEndpoints
{
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

    // Runs through the shared executor (which records the trace + detail), then applies
    // HTTP cache semantics (ETag / Cache-Control / 304) from the result.
    private static async Task<IResult> GetProduct(int id, HttpContext ctx, IRequestExecutor exec, CancellationToken ct)
    {
        var correlationId = ctx.Items[CorrelationConstants.CorrelationIdItemKey] as string;
        var execution = await exec.GetProductAsync(id, "Http", correlationId, ct);
        var result = execution.Result;

        ctx.Response.Headers[CorrelationConstants.ServedByHeader] = result.ServedBy.ToString();
        if (!result.Found)
        {
            return Results.NotFound();
        }

        var product = result.Value!;
        var etag = new EntityTagHeaderValue($"\"{product.Id}-v{product.Version}\"", isWeak: true);
        ctx.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromSeconds(PublicMaxAgeSeconds)
        };
        ctx.Response.GetTypedHeaders().ETag = etag;

        var inm = ctx.Request.GetTypedHeaders().IfNoneMatch;
        if (inm is not null && inm.Any(t => t.Compare(etag, useStrongComparison: false)))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Ok(product);
    }

    public sealed record ProductUpdate(decimal? Price, int? Stock);
}
