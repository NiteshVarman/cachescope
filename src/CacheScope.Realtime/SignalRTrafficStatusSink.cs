using CacheScope.Shared.Traffic;
using Microsoft.AspNetCore.SignalR;

namespace CacheScope.Realtime;

/// <summary>Broadcasts traffic-run status to all clients (the Live Traffic Panel).</summary>
public sealed class SignalRTrafficStatusSink(IHubContext<TraceHub> hub) : ITrafficStatusSink
{
    public const string ReceiveTrafficStatus = "ReceiveTrafficStatus";

    public async ValueTask PublishAsync(TrafficRunStatus status, CancellationToken ct = default)
    {
        await hub.Clients.All.SendAsync(ReceiveTrafficStatus, status, ct);
    }
}
