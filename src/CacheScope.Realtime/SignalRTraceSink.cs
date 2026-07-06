using System.Threading.Channels;
using CacheScope.Shared.Analytics;
using CacheScope.Shared.Tracing;
using Microsoft.Extensions.Options;

namespace CacheScope.Realtime;

/// <summary>
/// The realtime sink. Stats are recorded synchronously (cheap, always accurate); the
/// trace itself is queued to a bounded channel for batched broadcast. Under extreme load
/// the channel drops the oldest queued traces rather than blocking the request path — the
/// live stream is best-effort, the stats are not.
/// </summary>
public sealed class SignalRTraceSink : ITraceSink
{
    private readonly ILiveStats _stats;
    private readonly Channel<RequestTrace> _channel;

    public SignalRTraceSink(ILiveStats stats, IOptions<RealtimeOptions> options)
    {
        _stats = stats;
        _channel = Channel.CreateBounded<RequestTrace>(new BoundedChannelOptions(options.Value.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Consumed by the broadcast background service.</summary>
    public ChannelReader<RequestTrace> Reader => _channel.Reader;

    public ValueTask PublishAsync(RequestTrace trace, CancellationToken ct = default)
    {
        _stats.Record(trace);
        _channel.Writer.TryWrite(trace); // never blocks; drops oldest when full
        return ValueTask.CompletedTask;
    }
}
