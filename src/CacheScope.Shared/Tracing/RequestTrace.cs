namespace CacheScope.Shared.Tracing;

/// <summary>
/// The canonical record of a single request's journey through the cache layers.
/// Emitted for every request and streamed to clients over SignalR (Phase 2).
/// </summary>
public sealed record RequestTrace
{
    /// <summary>Monotonic per-run request identifier (e.g. #4512 in the live stream).</summary>
    public required long RequestId { get; init; }

    /// <summary>Correlation id tying this trace to logs and the OpenTelemetry trace.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>UTC timestamp when the request entered the pipeline.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>HTTP method (GET / POST / ...).</summary>
    public required string Method { get; init; }

    /// <summary>The request path, e.g. /products/45.</summary>
    public required string Path { get; init; }

    /// <summary>The layer that ultimately served the response.</summary>
    public required CacheLayer ServedBy { get; init; }

    /// <summary>Hit or Miss at the serving layer.</summary>
    public required CacheOutcome Outcome { get; init; }

    /// <summary>Total wall-clock response time in milliseconds.</summary>
    public required double ResponseTimeMs { get; init; }

    /// <summary>Cloudflare's CF-Cache-Status value when present (HIT/MISS/EXPIRED/DYNAMIC/BYPASS).</summary>
    public string? CfCacheStatus { get; init; }

    /// <summary>HTTP status code of the response.</summary>
    public int StatusCode { get; init; }

    /// <summary>Origin of the request: "Http" (real client), "Origin" or "Edge" (traffic generator).</summary>
    public string? Source { get; init; }
}
