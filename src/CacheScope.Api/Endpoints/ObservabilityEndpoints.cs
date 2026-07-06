using CacheScope.Shared.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CacheScope.Api.Endpoints;

public static class ObservabilityEndpoints
{
    public static IEndpointRouteBuilder MapObservabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/traces");

        group.MapGet("/recent", (IRequestDetailStore store, int? max) =>
            Results.Ok(store.Recent(max ?? 100)))
            .WithName("RecentTraces");

        // Per-request forensics: the timing waterfall + trace id for one correlation id.
        group.MapGet("/{correlationId}", (string correlationId, IRequestDetailStore store) =>
        {
            var detail = store.Get(correlationId);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        }).WithName("TraceDetail");

        return app;
    }
}
