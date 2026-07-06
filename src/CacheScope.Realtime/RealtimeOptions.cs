namespace CacheScope.Realtime;

public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>Max traces buffered for streaming before the oldest are dropped (stream is best-effort).</summary>
    public int ChannelCapacity { get; set; } = 20_000;

    /// <summary>Max traces sent in a single SignalR message.</summary>
    public int MaxBatchSize { get; set; } = 250;

    /// <summary>How often to flush a batch of traces, in milliseconds.</summary>
    public int FlushIntervalMs { get; set; } = 100;

    /// <summary>How often to broadcast the rolling stats snapshot, in milliseconds.</summary>
    public int StatsIntervalMs { get; set; } = 250;
}
