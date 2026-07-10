using CacheScope.Database;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CacheScope.Host.HealthChecks;

/// <summary>
/// Readiness check for the L4 database (embedded SQLite). Confirms the DbContext can open the
/// database file; since SQLite is in-process this is fast and has no cold-start behaviour.
/// </summary>
public sealed class SqlHealthCheck(CacheScopeDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy("SQL reachable")
                : HealthCheckResult.Unhealthy("SQL not reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQL check failed", ex);
        }
    }
}
