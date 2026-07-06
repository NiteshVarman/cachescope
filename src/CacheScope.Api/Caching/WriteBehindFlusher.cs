using CacheScope.Database;
using CacheScope.Shared.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CacheScope.Api.Caching;

/// <summary>
/// Drains the write-behind queue and persists buffered writes to the database. This is what
/// makes Write-Behind eventually-consistent: the cache is updated synchronously on the request
/// path, while the (slower) database write happens here, off the hot path.
/// </summary>
public sealed class WriteBehindFlusher(
    IWriteBehindQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<WriteBehindFlusher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<IProductStore>();
                    await store.UpdateAsync(item.ProductId, p => p.Price = item.Price, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Write-behind persist failed for product {Id}", item.ProductId);
                }
                finally
                {
                    (queue as WriteBehindQueue)?.MarkFlushed();
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
