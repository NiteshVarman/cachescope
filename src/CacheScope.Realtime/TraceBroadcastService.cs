using CacheScope.Shared.Analytics;
using CacheScope.Shared.Tracing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheScope.Realtime;

/// <summary>
/// Drains the trace channel and broadcasts to SignalR clients in two cadences:
///   - traces are batched and flushed every FlushIntervalMs (bounds fan-out cost at high RPS)
///   - a stats snapshot is pushed every StatsIntervalMs
/// A single broadcaster fanning out batches keeps us from sending N messages/sec/client.
/// </summary>
public sealed class TraceBroadcastService(
    SignalRTraceSink sink,
    IHubContext<TraceHub> hub,
    ILiveStats stats,
    IDatabaseMetrics dbMetrics,
    IMetricsTimeline timeline,
    IOptions<RealtimeOptions> options,
    ILogger<TraceBroadcastService> logger) : BackgroundService
{
    private readonly RealtimeOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var traceLoop = BroadcastTracesAsync(stoppingToken);
        var statsLoop = BroadcastStatsAsync(stoppingToken);
        var timelineLoop = SampleTimelineAsync(stoppingToken);
        await Task.WhenAll(traceLoop, statsLoop, timelineLoop);
    }

    // Once per second, derive per-second RPS / windowed latency / DB QPS from the
    // cumulative counters, append a timeline point, and broadcast it for the charts.
    private async Task SampleTimelineAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var prevTotal = 0L;
        var prevLatencySum = 0.0;
        var prevDbQueries = 0L;
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var s = stats.Snapshot();
                var dbQueries = dbMetrics.QueriesExecuted;

                var deltaTotal = s.TotalRequests - prevTotal;
                var cumLatencySum = s.AverageLatencyMs * s.TotalRequests;
                var windowLatencySum = cumLatencySum - prevLatencySum;

                var point = new MetricsTimelinePoint
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    RequestsPerSecond = deltaTotal,
                    AverageLatencyMs = deltaTotal <= 0 ? 0 : windowLatencySum / deltaTotal,
                    CacheHitRatio = s.CacheHitRatio,
                    DatabaseQueriesPerSecond = dbQueries - prevDbQueries
                };
                timeline.Add(point);
                await hub.Clients.All.SendAsync(TraceHub.ReceiveTimeline, point, ct);

                prevTotal = s.TotalRequests;
                prevLatencySum = cumLatencySum;
                prevDbQueries = dbQueries;
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Timeline sampling loop failed");
        }
    }

    private async Task BroadcastTracesAsync(CancellationToken ct)
    {
        var reader = sink.Reader;
        var buffer = new List<RequestTrace>(_options.MaxBatchSize);

        try
        {
            while (await reader.WaitToReadAsync(ct))
            {
                // Let a little time pass so bursts coalesce into one batch.
                await Task.Delay(_options.FlushIntervalMs, ct);

                buffer.Clear();
                while (buffer.Count < _options.MaxBatchSize && reader.TryRead(out var trace))
                {
                    buffer.Add(trace);
                }

                if (buffer.Count > 0)
                {
                    await hub.Clients.All.SendAsync(TraceHub.ReceiveTraces, buffer, ct);
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Trace broadcast loop failed");
        }
    }

    private async Task BroadcastStatsAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.StatsIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await hub.Clients.All.SendAsync(TraceHub.ReceiveStats, stats.Snapshot(), ct);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stats broadcast loop failed");
        }
    }
}
