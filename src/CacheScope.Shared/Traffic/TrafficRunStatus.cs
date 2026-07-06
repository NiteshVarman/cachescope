namespace CacheScope.Shared.Traffic;

public enum TrafficRunState { Idle, Preparing, Running, Completed, Cancelled, Failed }

/// <summary>Live snapshot of a traffic run, broadcast to the Live Traffic Panel.</summary>
public sealed record TrafficRunStatus
{
    public required string RunId { get; init; }
    public required TrafficRunState State { get; init; }
    public TrafficPattern Pattern { get; init; }
    public TrafficMode Mode { get; init; }

    public int? TargetTotal { get; init; }
    public long Completed { get; init; }
    public long Failed { get; init; }
    public long Pending { get; init; }

    public double CurrentRps { get; init; }
    public double AverageLatencyMs { get; init; }
    public double PeakLatencyMs { get; init; }
    public double ElapsedMs { get; init; }

    public string? Message { get; init; }

    public static TrafficRunStatus Idle() =>
        new() { RunId = "", State = TrafficRunState.Idle };
}
