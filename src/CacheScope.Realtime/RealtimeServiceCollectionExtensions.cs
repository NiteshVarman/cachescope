using System.Text.Json.Serialization;
using CacheScope.Shared.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheScope.Realtime;

public static class RealtimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers SignalR, the trace hub, the realtime sink (added as an ITraceSink so the
    /// pipeline fans traces to it), and the broadcast background service.
    /// </summary>
    public static IServiceCollection AddRealtime(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<RealtimeOptions>(config.GetSection(RealtimeOptions.SectionName));

        // Serialize enums (CacheLayer, CacheOutcome) as their string names so the
        // TypeScript client sees "Database"/"Memory", not raw integers.
        services.AddSignalR()
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // One shared sink instance: used both as an ITraceSink and by the broadcast service.
        services.AddSingleton<SignalRTraceSink>();
        services.AddSingleton<ITraceSink>(sp => sp.GetRequiredService<SignalRTraceSink>());
        services.AddHostedService<TraceBroadcastService>();

        // Traffic-run status broadcast.
        services.AddSingleton<Shared.Traffic.ITrafficStatusSink, SignalRTrafficStatusSink>();

        return services;
    }

    /// <summary>Maps the SignalR hub endpoint.</summary>
    public static IEndpointRouteBuilder MapRealtime(this IEndpointRouteBuilder app)
    {
        app.MapHub<TraceHub>("/hubs/traces");
        return app;
    }
}
