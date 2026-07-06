namespace CacheScope.Shared.Analytics;

/// <summary>One second of aggregated traffic, for time-series charts.</summary>
public sealed record MetricsTimelinePoint
{
    public required DateTimeOffset Timestamp { get; init; }
    public required double RequestsPerSecond { get; init; }
    public required double AverageLatencyMs { get; init; }
    public required double CacheHitRatio { get; init; }
    public long DatabaseQueriesPerSecond { get; init; }
}

/// <summary>A rolling window of per-second metric points.</summary>
public interface IMetricsTimeline
{
    void Add(MetricsTimelinePoint point);
    IReadOnlyList<MetricsTimelinePoint> Recent();
    void Reset();
}
