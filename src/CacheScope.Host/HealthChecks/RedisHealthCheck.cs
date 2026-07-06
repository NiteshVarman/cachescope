using CacheScope.RedisCache;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CacheScope.Host.HealthChecks;

public sealed class RedisHealthCheck(IRedisCacheLayer redis) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.FromResult(redis.IsConnected
            ? HealthCheckResult.Healthy("Redis connected")
            : HealthCheckResult.Unhealthy("Redis not connected"));
}
