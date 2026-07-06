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
    IOptions<RealtimeOptions> options,
    ILogger<TraceBroadcastService> logger) : BackgroundService
{
    private readonly RealtimeOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var traceLoop = BroadcastTracesAsync(stoppingToken);
        var statsLoop = BroadcastStatsAsync(stoppingToken);
        await Task.WhenAll(traceLoop, statsLoop);
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
