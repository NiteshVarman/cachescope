namespace CacheScope.Shared.Traffic;

/// <summary>Receives traffic-run status updates for broadcast (implemented in the Realtime layer).</summary>
public interface ITrafficStatusSink
{
    ValueTask PublishAsync(TrafficRunStatus status, CancellationToken ct = default);
}
