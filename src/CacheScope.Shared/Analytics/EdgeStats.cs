namespace CacheScope.Shared.Analytics;

/// <summary>
/// L0 (Cloudflare edge) statistics, pulled from the Cloudflare GraphQL Analytics API.
/// These requests never reach the origin, so they can't be counted in the pipeline —
/// they're fetched out-of-band and are aggregated + minutes-delayed by nature.
/// </summary>
public sealed record EdgeStatsSnapshot
{
    public bool Configured { get; init; }

    public long Hit { get; init; }
    public long Miss { get; init; }
    public long Expired { get; init; }
    public long Revalidated { get; init; }
    public long Dynamic { get; init; }
    public long None { get; init; }

    /// <summary>All requests Cloudflare saw in the window.</summary>
    public long Total { get; init; }

    /// <summary>hit / (hit + miss + expired + revalidated) — share of cacheable requests served by the edge.</summary>
    public double EdgeHitRatio { get; init; }

    public int WindowMinutes { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? Message { get; init; }

    public static EdgeStatsSnapshot NotConfigured() =>
        new() { Configured = false, Message = "Cloudflare analytics not configured (no token/zone)." };
}

/// <summary>Holds the latest edge snapshot, refreshed by a background poller.</summary>
public interface IEdgeStatsCache
{
    EdgeStatsSnapshot Current { get; }
    void Update(EdgeStatsSnapshot snapshot);
}

public sealed class EdgeStatsCache : IEdgeStatsCache
{
    private volatile EdgeStatsSnapshot _current = EdgeStatsSnapshot.NotConfigured();
    public EdgeStatsSnapshot Current => _current;
    public void Update(EdgeStatsSnapshot snapshot) => _current = snapshot;
}
