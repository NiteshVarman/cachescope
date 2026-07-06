using CacheScope.Shared.Caching;

namespace CacheScope.Shared.Tracing;

/// <summary>Time spent at one layer while serving a request (a bar in the waterfall).</summary>
public sealed record LayerTiming
{
    public required CacheLayer Layer { get; init; }
    public required double Ms { get; init; }
    public required CacheOutcome Outcome { get; init; }
}

/// <summary>
/// Full per-request forensics: the correlation id, the OpenTelemetry trace id, and the
/// per-layer timing waterfall. Recorded for recent requests and looked up on demand.
/// </summary>
public sealed record RequestDetail
{
    public required long RequestId { get; init; }
    public required string CorrelationId { get; init; }
    public string? TraceId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required CacheLayer ServedBy { get; init; }
    public required CacheOutcome Outcome { get; init; }
    public required double TotalMs { get; init; }
    public int StatusCode { get; init; }
    public string? Source { get; init; }
    public required IReadOnlyList<LayerTiming> Segments { get; init; }
}

/// <summary>Keeps recent <see cref="RequestDetail"/>s, looked up by correlation id.</summary>
public interface IRequestDetailStore
{
    void Record(RequestDetail detail);
    RequestDetail? Get(string correlationId);
    IReadOnlyList<RequestDetail> Recent(int max = 100);
}
