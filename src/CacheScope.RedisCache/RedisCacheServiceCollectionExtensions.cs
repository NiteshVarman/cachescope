using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace CacheScope.RedisCache;

public static class RedisCacheServiceCollectionExtensions
{
    /// <summary>Registers the L3 Redis layer with a resilient shared multiplexer.</summary>
    public static IServiceCollection AddRedisCacheLayer(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<RedisCacheOptions>(config.GetSection(RedisCacheOptions.SectionName));

        var connectionString = config.GetConnectionString("Redis") ?? "localhost:6379";
        var configuration = ConfigurationOptions.Parse(connectionString);
        // Don't crash startup if Redis is momentarily unavailable — reconnect in the background.
        configuration.AbortOnConnectFail = false;

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration));
        services.AddSingleton<IRedisCacheLayer, RedisCacheLayer>();
        return services;
    }
}
