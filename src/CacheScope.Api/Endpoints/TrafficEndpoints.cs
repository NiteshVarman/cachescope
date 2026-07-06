using CacheScope.Shared.Traffic;
using CacheScope.TrafficGenerator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CacheScope.Api.Endpoints;

public static class TrafficEndpoints
{
    public static IEndpointRouteBuilder MapTrafficEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/traffic");

        group.MapPost("/start", (TrafficConfig config, ITrafficRunner runner) =>
        {
            try
            {
                var runId = runner.Start(config);
                return Results.Ok(new { runId });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        }).WithName("StartTraffic");

        group.MapPost("/stop", (ITrafficRunner runner) =>
        {
            runner.Stop();
            return Results.Ok(runner.CurrentStatus());
        }).WithName("StopTraffic");

        group.MapGet("/status", (ITrafficRunner runner) => Results.Ok(runner.CurrentStatus()))
            .WithName("TrafficStatus");

        return app;
    }
}
