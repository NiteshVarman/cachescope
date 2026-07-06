using CacheScope.Api.Caching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CacheScope.Api.Endpoints;

public static class StampedeEndpoints
{
    public static IEndpointRouteBuilder MapStampedeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/stampede", async (StampedeRunner runner, int? hotKeyId, int? concurrency, CancellationToken ct) =>
        {
            try
            {
                var result = await runner.RunAsync(hotKeyId ?? 1, concurrency ?? 1000, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        }).WithName("RunStampede");

        return app;
    }
}
