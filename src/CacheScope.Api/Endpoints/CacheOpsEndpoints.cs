using CacheScope.Shared.Caching;
using CacheScope.Shared.Operations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CacheScope.Api.Endpoints;

public static class CacheOpsEndpoints
{
    public static IEndpointRouteBuilder MapCacheOpsEndpoints(this IEndpointRouteBuilder app)
    {
        var ops = app.MapGroup("/api/cache");

        ops.MapPost("/clear-memory", (ICacheOperations o) => Results.Ok(o.ClearMemory()));
        ops.MapPost("/clear-redis", async (ICacheOperations o, CancellationToken ct) => Results.Ok(await o.ClearRedisAsync(ct)));
        ops.MapPost("/warm-memory", async (ICacheOperations o, CancellationToken ct) => Results.Ok(await o.WarmMemoryAsync(ct)));
        ops.MapPost("/warm-redis", async (ICacheOperations o, CancellationToken ct) => Results.Ok(await o.WarmRedisAsync(ct)));
        ops.MapPost("/flush", async (ICacheOperations o, CancellationToken ct) => Results.Ok(await o.FlushAllAsync(ct)));
        ops.MapPost("/purge-cloudflare", async (ICacheOperations o, CancellationToken ct) => Results.Ok(await o.PurgeCloudflareAsync(ct)));
        ops.MapPost("/expire/{id:int}", async (int id, ICacheOperations o, CancellationToken ct) => Results.Ok(await o.ExpireProductAsync(id, ct)));
        ops.MapPost("/invalidate/{id:int}", async (int id, ICacheOperations o, CancellationToken ct) => Results.Ok(await o.InvalidateProductAsync(id, ct)));

        // Cache policy (TTLs, expiration mode, write strategy) — read + runtime update.
        var policy = app.MapGroup("/api/policy");
        policy.MapGet("/", (ICachePolicy p) => Results.Ok(p.Snapshot()));
        policy.MapPut("/", (CachePolicyUpdate update, ICachePolicy p) => Results.Ok(p.Apply(update)));

        return app;
    }
}
