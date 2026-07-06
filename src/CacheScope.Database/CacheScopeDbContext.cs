using CacheScope.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace CacheScope.Database;

public sealed class CacheScopeDbContext(DbContextOptions<CacheScopeDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var product = modelBuilder.Entity<Product>();
        product.HasKey(p => p.Id);
        product.Property(p => p.Name).HasMaxLength(128).IsRequired();
        product.Property(p => p.Category).HasMaxLength(64).IsRequired();
        product.Property(p => p.Price).HasPrecision(10, 2);

        product.HasData(ProductSeed.Generate());
    }
}
