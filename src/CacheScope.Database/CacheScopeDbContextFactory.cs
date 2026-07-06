using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CacheScope.Database;

/// <summary>
/// Design-time factory used by `dotnet ef`. Keeps migration tooling independent of the
/// Host's startup pipeline — the connection string here is only used to build the model,
/// never to connect. Real connections come from configuration at runtime.
/// </summary>
public sealed class CacheScopeDbContextFactory : IDesignTimeDbContextFactory<CacheScopeDbContext>
{
    public CacheScopeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CacheScopeDbContext>()
            .UseSqlServer("Server=localhost;Database=CacheScope;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new CacheScopeDbContext(options);
    }
}
