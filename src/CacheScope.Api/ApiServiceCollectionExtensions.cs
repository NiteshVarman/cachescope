using CacheScope.Api.Caching;
using CacheScope.Api.Tracing;
using CacheScope.Database;
using CacheScope.MemoryCache;
using CacheScope.RedisCache;
using CacheScope.Shared.Caching;
using CacheScope.Shared.Operations;
using CacheScope.Shared.Traffic;
using CacheScope.Shared.Tracing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheScope.Api;

public static class ApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the whole cache pipeline: L2/L3/L4 layers, the cache-aside orchestrator,
    /// and trace infrastructure. This is the single entry point the Host calls.
    /// </summary>
    public static IServiceCollection AddCacheScopePipeline(this IServiceCollection services, IConfiguration config)
    {
        // Runtime-mutable cache policy (TTLs, expiration mode, write strategy).
        services.AddSingleton<ICachePolicy, CachePolicyState>();

        services.AddMemoryCacheLayer(config);
        services.AddRedisCacheLayer(config);
        services.AddDatabaseLayer(config);

        services.AddScoped<IProductCacheService, ProductCacheService>();
        services.AddScoped<IRequestExecutor, RequestExecutor>();
        services.AddScoped<ITrafficSupport, TrafficSupport>();
        services.AddScoped<ICacheOperations, CacheOperations>();

        services.AddSingleton<IRequestCounter, RequestCounter>();
        services.AddSingleton<ITraceSink, LoggingTraceSink>();

        // Write-behind persistence queue + background flusher, and refresh-ahead scheduler.
        services.AddSingleton<IWriteBehindQueue, WriteBehindQueue>();
        services.AddHostedService<WriteBehindFlusher>();
        services.AddSingleton<RefreshAheadScheduler>();

        return services;
    }
}
