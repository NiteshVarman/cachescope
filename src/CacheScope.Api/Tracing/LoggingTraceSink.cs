using CacheScope.Shared.Tracing;
using Microsoft.Extensions.Logging;

namespace CacheScope.Api.Tracing;

/// <summary>The Phase 1 sink: writes each trace to the log. Phase 2 adds a SignalR sink alongside it.</summary>
public sealed class LoggingTraceSink(ILogger<LoggingTraceSink> logger) : ITraceSink
{
    public ValueTask PublishAsync(RequestTrace trace, CancellationToken ct = default)
    {
        logger.LogInformation(
            "#{RequestId} {Method} {Path} -> {ServedBy} ({Outcome}) {ResponseTimeMs:F1}ms [corr={CorrelationId}]",
            trace.RequestId, trace.Method, trace.Path, trace.ServedBy, trace.Outcome,
            trace.ResponseTimeMs, trace.CorrelationId);
        return ValueTask.CompletedTask;
    }
}
