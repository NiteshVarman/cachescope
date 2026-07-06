using CacheScope.Shared.Analytics;
using Microsoft.AspNetCore.SignalR;

namespace CacheScope.Realtime;

/// <summary>
/// The client-facing hub. Clients receive:
///   - "ReceiveTraces": batches of RequestTrace as they happen
///   - "ReceiveStats":  periodic LiveStatsSnapshot
/// On connect a client is immediately sent the current stats so its UI isn't empty.
/// </summary>
public sealed class TraceHub(ILiveStats stats) : Hub
{
    public const string ReceiveTraces = "ReceiveTraces";
    public const string ReceiveStats = "ReceiveStats";
    public const string ReceiveTimeline = "ReceiveTimeline";

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync(ReceiveStats, stats.Snapshot());
        await base.OnConnectedAsync();
    }

    /// <summary>Lets a client pull the latest stats on demand.</summary>
    public LiveStatsSnapshot GetStats() => stats.Snapshot();
}
