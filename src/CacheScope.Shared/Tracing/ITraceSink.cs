namespace CacheScope.Shared.Tracing;

/// <summary>
/// Receives a <see cref="RequestTrace"/> for every request. Phase 1 logs them;
/// Phase 2 streams them over SignalR; Phase 4 aggregates them for analytics.
/// Multiple sinks can be registered and fanned out to.
/// </summary>
public interface ITraceSink
{
    ValueTask PublishAsync(RequestTrace trace, CancellationToken ct = default);
}

/// <summary>Monotonic per-process source of request ids (the #4512 in the live stream).</summary>
public interface IRequestCounter
{
    long Next();
    long Current { get; }
}
