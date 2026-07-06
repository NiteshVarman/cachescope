using System.Threading.Channels;

namespace CacheScope.Shared.Caching;

/// <summary>A pending database write buffered by the write-behind strategy.</summary>
public sealed record WriteBehindItem(int ProductId, decimal Price);

public interface IWriteBehindQueue
{
    void Enqueue(WriteBehindItem item);
    ChannelReader<WriteBehindItem> Reader { get; }
    long PendingApprox { get; }
}

public sealed class WriteBehindQueue : IWriteBehindQueue
{
    private readonly Channel<WriteBehindItem> _channel =
        Channel.CreateUnbounded<WriteBehindItem>(new UnboundedChannelOptions { SingleReader = true });
    private long _pending;

    public ChannelReader<WriteBehindItem> Reader => _channel.Reader;
    public long PendingApprox => Interlocked.Read(ref _pending);

    public void Enqueue(WriteBehindItem item)
    {
        if (_channel.Writer.TryWrite(item))
        {
            Interlocked.Increment(ref _pending);
        }
    }

    /// <summary>Called by the flusher after a write is persisted.</summary>
    public void MarkFlushed() => Interlocked.Decrement(ref _pending);
}
