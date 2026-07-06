using Microsoft.Extensions.DependencyInjection;

namespace CacheScope.TrafficGenerator;

public static class TrafficGeneratorServiceCollectionExtensions
{
    public static IServiceCollection AddTrafficGenerator(this IServiceCollection services)
    {
        // Singleton: it owns the single active run and is driven from the API endpoints.
        services.AddSingleton<TrafficRunner>();
        services.AddSingleton<ITrafficRunner>(sp => sp.GetRequiredService<TrafficRunner>());
        return services;
    }
}
