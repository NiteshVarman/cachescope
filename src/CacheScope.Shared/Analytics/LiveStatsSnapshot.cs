namespace CacheScope.Shared.Analytics;

/// <summary>A point-in-time rolling summary of traffic, broadcast to clients alongside traces.</summary>
public sealed record LiveStatsSnapshot
{
    public long TotalRequests { get; init; }

    public long CloudflareHits { get; init; }
    public long BrowserHits { get; init; }
    public long MemoryHits { get; init; }
    public long RedisHits { get; init; }
    public long DatabaseHits { get; init; }

    public long FailedRequests { get; init; }

    /// <summary>Share of requests served by any cache layer (L0/L1/L2/L3), 0..1.</summary>
    public double CacheHitRatio { get; init; }

    public double AverageLatencyMs { get; init; }
    public double PeakLatencyMs { get; init; }
}
