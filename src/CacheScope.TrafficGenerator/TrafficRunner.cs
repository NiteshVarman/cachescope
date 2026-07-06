using System.Diagnostics;
using CacheScope.Shared.Caching;
using CacheScope.Shared.Traffic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CacheScope.TrafficGenerator;

/// <summary>
/// The load-test engine. Self-paces dispatch to a target RPS, bounds in-flight requests
/// with a semaphore, prepares caches per pattern, and broadcasts live run status. Only one
/// run is active at a time. Generated requests go through the same IRequestExecutor as real
/// traffic, so they appear in the same live stream and analytics.
/// </summary>
public sealed class TrafficRunner(
    IServiceScopeFactory scopeFactory,
    ITrafficStatusSink statusSink,
    ILogger<TrafficRunner> logger) : ITrafficRunner
{
    private const int StatusIntervalMs = 250;

    private readonly Lock _gate = new();
    private CancellationTokenSource? _cts;
    private volatile RunMetrics _metrics = new();
    private volatile string _runId = "";
    private TrafficConfig _config = new();
    private TrafficRunState _state = TrafficRunState.Idle;
    private long _startTimestamp;
    private volatile string? _message;
    private double _currentRps;

    public string Start(TrafficConfig config)
    {
        lock (_gate)
        {
            if (_state is TrafficRunState.Preparing or TrafficRunState.Running)
            {
                throw new InvalidOperationException("A traffic run is already in progress.");
            }

            _runId = Guid.NewGuid().ToString("n")[..12];
            _config = Normalize(config);
            _metrics = new RunMetrics();
            _state = TrafficRunState.Preparing;
            _message = null;
            _currentRps = 0;
            _cts = new CancellationTokenSource();
        }

        var runId = _runId;
        var ct = _cts!.Token;
        _ = Task.Run(() => RunAsync(runId, _config, ct), CancellationToken.None);
        return runId;
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
        }
    }

    public TrafficRunStatus CurrentStatus()
    {
        var m = _metrics;
        var elapsedMs = _state is TrafficRunState.Idle
            ? 0
            : Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;

        return new TrafficRunStatus
        {
            RunId = _runId,
            State = _state,
            Pattern = _config.Pattern,
            Mode = _config.Mode,
            TargetTotal = _config.TotalRequests,
            Completed = m.Completed,
            Failed = m.Failed,
            Pending = m.Pending,
            CurrentRps = _currentRps,
            AverageLatencyMs = m.AverageLatencyMs,
            PeakLatencyMs = m.PeakLatencyMs,
            ElapsedMs = elapsedMs,
            Message = _message
        };
    }

    private async Task RunAsync(string runId, TrafficConfig config, CancellationToken ct)
    {
        var statusLoop = BroadcastStatusLoopAsync(ct);
        try
        {
            await PrepareAsync(config, ct);

            var universe = await WithScopeAsync(s =>
                s.GetRequiredService<ITrafficSupport>().GetKeyUniverseAsync(ct));
            if (universe.Count == 0)
            {
                _message = "No products to target.";
                _state = TrafficRunState.Failed;
                return;
            }

            var selector = new TrafficKeySelector(universe, config, Random.Shared);

            _state = TrafficRunState.Running;
            _startTimestamp = Stopwatch.GetTimestamp();

            await DispatchLoopAsync(config, selector, ct);

            _state = ct.IsCancellationRequested ? TrafficRunState.Cancelled : TrafficRunState.Completed;
        }
        catch (OperationCanceledException)
        {
            _state = TrafficRunState.Cancelled;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Traffic run {RunId} failed", runId);
            _message = ex.Message;
            _state = TrafficRunState.Failed;
        }
        finally
        {
            await PublishStatusAsync(CancellationToken.None);
            await statusLoop.ConfigureAwait(false);
        }
    }

    private async Task PrepareAsync(TrafficConfig config, CancellationToken ct)
    {
        // Always wake a paused serverless DB so its cold-start doesn't skew latency.
        await WithScopeAsync(s => s.GetRequiredService<ITrafficSupport>().ResumeDatabaseAsync(ct));

        switch (config.Pattern)
        {
            case TrafficPattern.ColdStart:
                await WithScopeAsync(async s =>
                {
                    var support = s.GetRequiredService<ITrafficSupport>();
                    support.ClearMemory();
                    await support.ClearDistributedAsync(ct);
                    return true;
                });
                break;

            case TrafficPattern.WarmCache:
                await WarmAsync(config, ct);
                break;

            case TrafficPattern.CacheStampede:
                // Warm the hot key, then expire it so the run stampedes a cold hot key.
                await WithScopeAsync(async s =>
                {
                    var universe = await s.GetRequiredService<ITrafficSupport>().GetKeyUniverseAsync(ct);
                    if (universe.Count > 0)
                    {
                        var exec = s.GetRequiredService<IRequestExecutor>();
                        await exec.GetProductAsync(universe[0], config.Mode.ToString(), ct: ct);
                        await s.GetRequiredService<ITrafficSupport>().ExpireAsync(universe[0], ct);
                    }
                    return true;
                });
                break;
        }
    }

    private async Task WarmAsync(TrafficConfig config, CancellationToken ct)
    {
        await WithScopeAsync(async s =>
        {
            var support = s.GetRequiredService<ITrafficSupport>();
            var exec = s.GetRequiredService<IRequestExecutor>();
            var universe = await support.GetKeyUniverseAsync(ct);
            foreach (var id in universe)
            {
                await exec.GetProductAsync(id, config.Mode.ToString(), ct: ct);
            }
            return true;
        });
    }

    private async Task DispatchLoopAsync(TrafficConfig config, TrafficKeySelector selector, CancellationToken ct)
    {
        using var concurrency = new SemaphoreSlim(config.Concurrency, config.Concurrency);

        var baseIntervalMs = config.RequestsPerSecond is > 0
            ? 1000.0 / config.RequestsPerSecond.Value
            : 0;
        var durationMs = config.DurationSeconds.HasValue ? config.DurationSeconds.Value * 1000.0 : (double?)null;
        var sw = Stopwatch.StartNew();
        double nextDueMs = 0;

        while (!ct.IsCancellationRequested)
        {
            var dispatched = _metrics.Dispatched;
            if (config.TotalRequests.HasValue && dispatched >= config.TotalRequests.Value) break;
            if (durationMs.HasValue && sw.Elapsed.TotalMilliseconds >= durationMs.Value) break;

            try { await concurrency.WaitAsync(ct); }
            catch (OperationCanceledException) { break; }

            _metrics.Dispatch();
            _ = ExecuteOneAsync(config, selector, ct)
                .ContinueWith(_ => concurrency.Release(), TaskScheduler.Default);

            if (baseIntervalMs > 0)
            {
                nextDueMs += baseIntervalMs * BurstFactor(config.Pattern, sw.Elapsed.TotalMilliseconds);
                var delay = nextDueMs - sw.Elapsed.TotalMilliseconds;
                if (delay > 1)
                {
                    try { await Task.Delay(TimeSpan.FromMilliseconds(delay), ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        // Drain in-flight requests.
        for (var i = 0; i < config.Concurrency; i++)
        {
            try { await concurrency.WaitAsync(CancellationToken.None); }
            catch { break; }
        }
    }

    private async Task ExecuteOneAsync(TrafficConfig config, TrafficKeySelector selector, CancellationToken ct)
    {
        var id = selector.Next();
        var isGet = Random.Shared.Next(100) < config.GetPercentage;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var exec = scope.ServiceProvider.GetRequiredService<IRequestExecutor>();
            var result = isGet
                ? await exec.GetProductAsync(id, config.Mode.ToString(), ct: ct)
                : await exec.UpdateProductAsync(id, RandomPrice(), config.Mode.ToString(), ct: ct);
            _metrics.Complete(result.Trace.ResponseTimeMs, failed: result.Trace.StatusCode >= 500);
        }
        catch (OperationCanceledException)
        {
            _metrics.Complete(0, failed: false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Generated request to product {Id} failed", id);
            _metrics.Complete(0, failed: true);
        }
    }

    // Burst: 1.5s of fast (0.3x interval) followed by 1.5s of slow (3x interval).
    private static double BurstFactor(TrafficPattern pattern, double elapsedMs)
    {
        if (pattern != TrafficPattern.Burst) return 1.0;
        var phase = (int)(elapsedMs / 1500) % 2;
        return phase == 0 ? 0.3 : 3.0;
    }

    private static decimal RandomPrice() => Math.Round((decimal)(Random.Shared.NextDouble() * 500 + 1), 2);

    private async Task BroadcastStatusLoopAsync(CancellationToken ct)
    {
        var lastCompleted = 0L;
        var lastTs = Stopwatch.GetTimestamp();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(StatusIntervalMs, ct);

                var completed = _metrics.Completed;
                var now = Stopwatch.GetTimestamp();
                var dt = Stopwatch.GetElapsedTime(lastTs, now).TotalSeconds;
                if (dt > 0)
                {
                    _currentRps = (completed - lastCompleted) / dt;
                }
                lastCompleted = completed;
                lastTs = now;

                await PublishStatusAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* run ended */ }
    }

    private async Task PublishStatusAsync(CancellationToken ct)
    {
        try { await statusSink.PublishAsync(CurrentStatus(), ct); }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to publish traffic status"); }
    }

    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = scopeFactory.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private async Task WithScopeAsync(Func<IServiceProvider, Task> work)
    {
        using var scope = scopeFactory.CreateScope();
        await work(scope.ServiceProvider);
    }

    private static TrafficConfig Normalize(TrafficConfig config)
    {
        var total = config.TotalRequests;
        if (total is > TrafficConfig.MaxRequestsHardCap)
        {
            total = TrafficConfig.MaxRequestsHardCap;
        }

        // If nothing bounds the run, default to a 30s duration so it can't run forever.
        var duration = config.DurationSeconds;
        if (total is null && duration is null)
        {
            duration = 30;
        }

        return config with
        {
            TotalRequests = total,
            DurationSeconds = duration,
            Concurrency = Math.Clamp(config.Concurrency, 1, 2000),
            GetPercentage = Math.Clamp(config.GetPercentage, 0, 100),
            HotKeyCount = Math.Max(1, config.HotKeyCount)
        };
    }
}
