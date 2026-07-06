using CacheScope.Shared.Analytics;
using Microsoft.Extensions.DependencyInjection;

namespace CacheScope.Analytics;

public static class AnalyticsServiceCollectionExtensions
{
    public static IServiceCollection AddAnalytics(this IServiceCollection services)
    {
        services.AddSingleton<ILiveStats, LiveStats>();
        return services;
    }
}
