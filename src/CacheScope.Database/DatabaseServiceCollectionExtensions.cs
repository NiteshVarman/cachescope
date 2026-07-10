using CacheScope.Shared.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheScope.Database;

public static class DatabaseServiceCollectionExtensions
{
    /// <summary>Registers the L4 database layer: EF Core context, store, and metrics.</summary>
    public static IServiceCollection AddDatabaseLayer(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<DatabaseOptions>(config.GetSection(DatabaseOptions.SectionName));

        // Embedded SQLite. Default to a local file; container overrides via ConnectionStrings:Sql.
        var connectionString = config.GetConnectionString("Sql") ?? "Data Source=cachescope.db";
        services.AddDbContext<CacheScopeDbContext>(o => o.UseSqlite(connectionString));

        // Metrics are process-wide, so a singleton; the store is scoped to the DbContext.
        services.AddSingleton<IDatabaseMetrics, DatabaseMetrics>();
        services.AddScoped<IProductStore, ProductStore>();

        return services;
    }
}
