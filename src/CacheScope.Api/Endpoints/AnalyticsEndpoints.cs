using CacheScope.Shared.Analytics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CacheScope.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics");

        group.MapGet("/", (ILiveStats stats, IDatabaseMetrics db, IMetricsTimeline timeline) =>
            Results.Ok(new AnalyticsSnapshot
            {
                Stats = stats.Snapshot(),
                DatabaseQueriesExecuted = db.QueriesExecuted,
                DatabaseQueriesPrevented = db.QueriesPrevented,
                DatabaseAverageQueryTimeMs = db.AverageQueryTimeMs,
                Timeline = timeline.Recent()
            }))
            .WithName("GetAnalytics");

        // Reset all counters so two traffic patterns can be compared from a clean slate.
        group.MapPost("/reset", (ILiveStats stats, IDatabaseMetrics db, IMetricsTimeline timeline) =>
        {
            stats.Reset();
            db.Reset();
            timeline.Reset();
            return Results.NoContent();
        }).WithName("ResetAnalytics");

        return app;
    }
}
