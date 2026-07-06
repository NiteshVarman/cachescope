using CacheScope.Database;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CacheScope.Host.HealthChecks;

/// <summary>
/// Readiness check for Azure SQL. Note: against a paused serverless database the first
/// probe triggers a resume and can take tens of seconds — this is expected, not a fault.
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
