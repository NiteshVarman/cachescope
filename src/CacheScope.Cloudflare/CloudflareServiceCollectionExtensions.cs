using CacheScope.Shared.Analytics;
using CacheScope.Shared.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheScope.Cloudflare;

public static class CloudflareServiceCollectionExtensions
{
    public static IServiceCollection AddCloudflare(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<CloudflareOptions>(config.GetSection(CloudflareOptions.SectionName));
        services.AddHttpClient<ICloudflarePurger, CloudflarePurger>();

        // L0 edge-stats: a cached snapshot refreshed by a background poller from the CF GraphQL API.
        services.AddSingleton<IEdgeStatsCache, EdgeStatsCache>();
        services.AddHttpClient<CloudflareEdgeStatsClient>();
        services.AddHostedService<CloudflareEdgePoller>();
        return services;
    }
}
