namespace CacheScope.Shared.Analytics;

/// <summary>The full analytics picture served by GET /api/analytics.</summary>
public sealed record AnalyticsSnapshot
{
    public required LiveStatsSnapshot Stats { get; init; }

    // Database (L4) analytics.
    public long DatabaseQueriesExecuted { get; init; }
    public long DatabaseQueriesPrevented { get; init; }
    public double DatabaseAverageQueryTimeMs { get; init; }

    public required IReadOnlyList<MetricsTimelinePoint> Timeline { get; init; }

    /// <summary>L0 edge stats from Cloudflare (out-of-band, aggregated). Null/NotConfigured locally.</summary>
    public EdgeStatsSnapshot? CloudflareEdge { get; init; }
}
