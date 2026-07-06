using System.Diagnostics;
using CacheScope.Shared.Analytics;
using CacheScope.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CacheScope.Database;

public sealed class ProductStore(
    CacheScopeDbContext db,
    IDatabaseMetrics metrics,
    IOptions<DatabaseOptions> options) : IProductStore
{
    private readonly DatabaseOptions _options = options.Value;

    public async Task<Product?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        await MaybeSimulateLatencyAsync(ct);
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        sw.Stop();
        metrics.RecordQuery(sw.Elapsed.TotalMilliseconds);
        return product;
    }

    public async Task<IReadOnlyList<int>> GetAllIdsAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var ids = await db.Products.AsNoTracking().Select(p => p.Id).ToListAsync(ct);
        sw.Stop();
        metrics.RecordQuery(sw.Elapsed.TotalMilliseconds);
        return ids;
    }

    public async Task<Product?> UpdateAsync(int id, Action<Product> mutate, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            sw.Stop();
            metrics.RecordQuery(sw.Elapsed.TotalMilliseconds);
            return null;
        }

        mutate(product);
        product.Version++;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        sw.Stop();
        metrics.RecordQuery(sw.Elapsed.TotalMilliseconds);
        return product;
    }

    private async Task MaybeSimulateLatencyAsync(CancellationToken ct)
    {
        if (_options.SimulatedQueryLatencyMs > 0)
        {
            await Task.Delay(_options.SimulatedQueryLatencyMs, ct);
        }
    }
}
