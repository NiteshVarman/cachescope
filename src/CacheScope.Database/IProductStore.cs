using CacheScope.Shared.Models;

namespace CacheScope.Database;

/// <summary>L4 — the source of truth. Every call here is a real database round-trip.</summary>
public interface IProductStore
{
    Task<Product?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetAllIdsAsync(CancellationToken ct = default);
    Task<Product?> UpdateAsync(int id, Action<Product> mutate, CancellationToken ct = default);
}
