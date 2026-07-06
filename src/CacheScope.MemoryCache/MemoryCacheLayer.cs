using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CacheScope.MemoryCache;

/// <summary>
/// Wraps <see cref="IMemoryCache"/>. IMemoryCache has no native "clear all", so every
/// entry is linked to a shared cancellation token; cancelling it evicts them en masse.
/// </summary>
public sealed class MemoryCacheLayer(IMemoryCache cache, IOptionsMonitor<MemoryCacheLayerOptions> options)
    : IMemoryCacheLayer, IDisposable
{
    private readonly Lock _resetLock = new();
    private CancellationTokenSource _resetCts = new();

    public bool TryGet<T>(string key, out T? value)
    {
        if (cache.TryGetValue(key, out var stored) && stored is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public void Set<T>(string key, T value, TimeSpan? ttl = null)
    {
        var opts = options.CurrentValue;
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl ?? opts.DefaultTtl,
            SlidingExpiration = opts.SlidingExpiration
        };

        // Link to the current reset token so Clear() can evict this entry.
        CancellationTokenSource cts;
        lock (_resetLock)
        {
            cts = _resetCts;
        }
        entryOptions.AddExpirationToken(new Microsoft.Extensions.Primitives.CancellationChangeToken(cts.Token));

        cache.Set(key, value, entryOptions);
    }

    public void Remove(string key) => cache.Remove(key);

    public void Clear()
    {
        CancellationTokenSource old;
        lock (_resetLock)
        {
            old = _resetCts;
            _resetCts = new CancellationTokenSource();
        }
        old.Cancel();
        old.Dispose();
    }

    public void Dispose() => _resetCts.Dispose();
}
