using CacheScope.Shared.Models;

namespace CacheScope.Database;

/// <summary>
/// Deterministic seed of 100 products. Deterministic so it can live inside an EF
/// migration (HasData forbids non-constant values like DateTimeOffset.Now).
/// 100 products gives the traffic generator room for hot-key, top-10 and Zipf patterns.
/// </summary>
public static class ProductSeed
{
    public const int Count = 100;

    private static readonly string[] Categories =
        ["Electronics", "Books", "Home", "Toys", "Apparel", "Grocery", "Sports", "Beauty"];

    private static readonly DateTimeOffset SeedTimestamp =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static IEnumerable<Product> Generate()
    {
        for (var id = 1; id <= Count; id++)
        {
            yield return new Product
            {
                Id = id,
                Name = $"Product {id:D3}",
                Category = Categories[id % Categories.Length],
                Price = Math.Round(5m + (id * 3.5m % 500m), 2),
                Stock = 10 + (id * 7 % 990),
                Version = 1,
                UpdatedAt = SeedTimestamp
            };
        }
    }
}
