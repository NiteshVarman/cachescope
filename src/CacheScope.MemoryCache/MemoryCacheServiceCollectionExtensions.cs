using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheScope.MemoryCache;

public static class MemoryCacheServiceCollectionExtensions
{
    /// <summary>Registers the L2 in-process memory cache layer.</summary>
    public static IServiceCollection AddMemoryCacheLayer(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<MemoryCacheLayerOptions>(config.GetSection(MemoryCacheLayerOptions.SectionName));
        services.AddMemoryCache();
        services.AddSingleton<IMemoryCacheLayer, MemoryCacheLayer>();
        return services;
    }
}
