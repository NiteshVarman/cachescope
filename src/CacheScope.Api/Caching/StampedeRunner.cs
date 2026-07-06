using System.Diagnostics;
using CacheScope.Shared.Caching;
using CacheScope.Shared.Analytics;
using CacheScope.Shared.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CacheScope.Api.Caching;

/// <summary>
/// Runs the cache-stampede demonstration: expire a hot key, then fire N concurrent
/// requests for it — once with single-flight OFF (each miss hits the DB) and once with
/// it ON (all misses coalesce into one DB load). Returns the two DB-query counts.
/// </summary>
public sealed class StampedeRunner(
    IServiceScopeFactory scopeFactory,
    ICachePolicy policy,
    IDatabaseMetrics dbMetrics,
    ILogger<StampedeRunner> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<StampedeResult> RunAsync(int hotKeyId, int concurrency, CancellationToken ct = default)
    {
        concurrency = Math.Clamp(concurrency, 1, 2000);
        if (!await _gate.WaitAsync(0, ct))
        {
            throw new InvalidOperationException("A stampede demo is already running.");
        }

        var original = policy.StampedeProtection;
        try
        {
            // Run unprotected first, then protected, from an identically cold hot key.
            var unprotected = await RunScenarioAsync(false, hotKeyId, concurrency, ct);
            var protectedRun = await RunScenarioAsync(true, hotKeyId, concurrency, ct);

            return new StampedeResult
            {
                HotKeyId = hotKeyId,
                Unprotected = unprotected,
                Protected = protectedRun
            };
        }
        finally
        {
            policy.Apply(new CachePolicyUpdate { StampedeProtection = original });
            _gate.Release();
        }
    }

    private async Task<StampedeScenario> RunScenarioAsync(bool protect, int hotKeyId, int concurrency, CancellationToken ct)
    {
        policy.Apply(new CachePolicyUpdate { StampedeProtection = protect });

        // Start from a cold hot key so every request misses the cache.
        using (var scope = scopeFactory.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICacheOperations>().InvalidateProductAsync(hotKeyId, ct);
        }

        var before = dbMetrics.QueriesExecuted;
        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, concurrency).Select(_ => FireAsync(hotKeyId, ct));
        await Task.WhenAll(tasks);

        sw.Stop();
        var queries = dbMetrics.QueriesExecuted - before;
        logger.LogInformation("Stampede scenario protection={Protect} concurrency={N} -> {Queries} DB queries",
            protect, concurrency, queries);

        return new StampedeScenario
        {
            ProtectionEnabled = protect,
            Concurrency = concurrency,
            DatabaseQueries = queries,
            DurationMs = sw.Elapsed.TotalMilliseconds
        };
    }

    private async Task FireAsync(int hotKeyId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var exec = scope.ServiceProvider.GetRequiredService<IRequestExecutor>();
            await exec.GetProductAsync(hotKeyId, "Stampede", ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Stampede request failed");
        }
    }
}
