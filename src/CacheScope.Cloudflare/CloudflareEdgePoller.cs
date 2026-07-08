using CacheScope.Shared.Analytics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheScope.Cloudflare;

/// <summary>
/// Periodically refreshes the L0 edge-stats cache from Cloudflare. Runs only when
/// Cloudflare is configured; otherwise it publishes a "not configured" snapshot and stops.
/// </summary>
public sealed class CloudflareEdgePoller(
    CloudflareEdgeStatsClient client,
    IEdgeStatsCache cache,
    IOptions<CloudflareOptions> options,
    ILogger<CloudflareEdgePoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.IsConfigured)
        {
            cache.Update(EdgeStatsSnapshot.NotConfigured());
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(15, opts.AnalyticsPollSeconds)));
        try
        {
            do
            {
                try { cache.Update(await client.QueryAsync(stoppingToken)); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { logger.LogDebug(ex, "Edge-stats poll failed"); }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
