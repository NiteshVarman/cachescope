using CacheScope.Shared.Caching;
using CacheScope.Shared.Models;

namespace CacheScope.Api.Caching;

/// <summary>
/// Orchestrates the L2 → L3 → L4 read path (cache-aside) and reports which layer
/// served each request. This is the component the whole platform observes.
/// </summary>
public interface IProductCacheService
{
    Task<CacheReadResult<Product>> GetAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetIdsAsync(CancellationToken ct = default);

    /// <summary>Cache-aside write: update the source of truth, then invalidate L2/L3.</summary>
    Task<Product?> UpdateAsync(int id, Action<Product> mutate, CancellationToken ct = default);
}
