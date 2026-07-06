using System.Collections.Concurrent;
using CacheScope.Shared.Models;

namespace CacheScope.Api.Caching;

/// <summary>
/// Request coalescing (single-flight). Concurrent callers for the same key share one
/// in-flight load task, so a stampede of misses triggers exactly one database query
/// instead of one per caller. Singleton — the whole point is to coordinate across
/// concurrent requests. The key is evicted once its load completes.
/// </summary>
public sealed class SingleFlight
{
    private readonly ConcurrentDictionary<string, Lazy<Task<Product?>>> _inflight = new();

    public Task<Product?> RunAsync(string key, Func<Task<Product?>> load)
    {
        var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<Product?>>(() => LoadAndEvictAsync(key, load)));
        return lazy.Value;
    }

    private async Task<Product?> LoadAndEvictAsync(string key, Func<Task<Product?>> load)
    {
        try
        {
            return await load();
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }
}
