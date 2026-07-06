namespace CacheScope.Shared.Traffic;

/// <summary>User-configurable description of a traffic run (the load-test knobs).</summary>
public sealed record TrafficConfig
{
    public TrafficPattern Pattern { get; init; } = TrafficPattern.Steady;
    public TrafficMode Mode { get; init; } = TrafficMode.Origin;

    /// <summary>Total requests to send. Null = bound by duration only.</summary>
    public int? TotalRequests { get; init; } = 1000;

    /// <summary>Target requests per second. Null = as fast as concurrency allows.</summary>
    public int? RequestsPerSecond { get; init; } = 200;

    /// <summary>Max run duration in seconds. Null = bound by total only.</summary>
    public int? DurationSeconds { get; init; }

    /// <summary>Max in-flight requests.</summary>
    public int Concurrency { get; init; } = 20;

    /// <summary>Percentage of requests that are GETs; the remainder are writes (PUT).</summary>
    public int GetPercentage { get; init; } = 100;

    public KeySelectionMode KeySelection { get; init; } = KeySelectionMode.Random;

    /// <summary>Size of the "hot" key set for TopNHotKeys / Zipf / Mixed patterns.</summary>
    public int HotKeyCount { get; init; } = 10;

    /// <summary>Optional RNG seed for reproducible runs.</summary>
    public int? Seed { get; init; }

    /// <summary>A hard safety cap so an unbounded run can't spin forever.</summary>
    public const int MaxRequestsHardCap = 1_000_000;
}
