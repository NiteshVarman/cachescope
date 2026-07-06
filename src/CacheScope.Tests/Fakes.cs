using CacheScope.Database;
using CacheScope.MemoryCache;
using CacheScope.RedisCache;
using CacheScope.Shared.Analytics;
using CacheScope.Shared.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CacheScope.Tests;

/// <summary>Never actually used in these tests (refresh-ahead is off) — just satisfies the ctor.</summary>
public sealed class StubScopeFactory : IServiceScopeFactory
{
    public IServiceScope CreateScope() => throw new NotSupportedException();
}

/// <summary>In-memory L2 fake.</summary>
public sealed class FakeMemoryLayer : IMemoryCacheLayer
{
    private readonly Dictionary<string, object?> _store = new();

    public bool TryGet<T>(string key, out T? value)
    {
        if (_store.TryGetValue(key, out var v) && v is T t) { value = t; return true; }
        value = default;
        return false;
    }

    public void Set<T>(string key, T value, TimeSpan? ttl = null) => _store[key] = value;
    public void Remove(string key) => _store.Remove(key);
    public void Clear() => _store.Clear();
}

/// <summary>In-memory L3 fake.</summary>
public sealed class FakeRedisLayer : IRedisCacheLayer
{
    private readonly Dictionary<string, object?> _store = new();
    public bool IsConnected => true;

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(key, out var v) && v is T t ? t : default);

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) { _store.Remove(key); return Task.CompletedTask; }
    public Task<bool> ExpireNowAsync(string key, CancellationToken ct = default) => Task.FromResult(_store.Remove(key));
    public Task FlushAsync(CancellationToken ct = default) { _store.Clear(); return Task.CompletedTask; }
}

/// <summary>In-memory L4 fake that counts real queries.</summary>
public sealed class FakeProductStore : IProductStore
{
    private readonly Dictionary<int, Product> _products;
    public int QueryCount { get; private set; }

    public FakeProductStore(int count = 10)
    {
        _products = Enumerable.Range(1, count).ToDictionary(
            id => id,
            id => new Product { Id = id, Name = $"P{id}", Category = "Test", Price = id, Stock = 100, Version = 1 });
    }

    public Task<Product?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        QueryCount++;
        return Task.FromResult(_products.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<int>> GetAllIdsAsync(CancellationToken ct = default)
    {
        QueryCount++;
        return Task.FromResult<IReadOnlyList<int>>(_products.Keys.ToList());
    }

    public Task<Product?> UpdateAsync(int id, Action<Product> mutate, CancellationToken ct = default)
    {
        QueryCount++;
        if (!_products.TryGetValue(id, out var p)) return Task.FromResult<Product?>(null);
        mutate(p);
        p.Version++;
        return Task.FromResult<Product?>(p);
    }
}

public sealed class FakeDbMetrics : IDatabaseMetrics
{
    public long QueriesExecuted { get; private set; }
    public long QueriesPrevented { get; private set; }
    public double TotalQueryTimeMs { get; private set; }
    public double AverageQueryTimeMs => QueriesExecuted == 0 ? 0 : TotalQueryTimeMs / QueriesExecuted;
    public void RecordQuery(double elapsedMs) { QueriesExecuted++; TotalQueryTimeMs += elapsedMs; }
    public void RecordPrevented() => QueriesPrevented++;
    public void Reset() { QueriesExecuted = 0; QueriesPrevented = 0; TotalQueryTimeMs = 0; }
}
