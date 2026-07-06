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
        return services;
    }
}
