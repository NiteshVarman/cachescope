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

    public double P50LatencyMs { get; init; }
    public double P95LatencyMs { get; init; }
    public double P99LatencyMs { get; init; }

    /// <summary>Tally of observed CF-Cache-Status header values (HIT/MISS/EXPIRED/DYNAMIC/BYPASS/NONE).</summary>
    public IReadOnlyDictionary<string, long> CfCacheStatusCounts { get; init; } =
        new Dictionary<string, long>();

    /// <summary>Cloudflare edge hit ratio (CF HIT / requests with a CF status), 0..1.</summary>
    public double EdgeHitRatio { get; init; }
}
