using System.Diagnostics;
using CacheScope.Api.Caching;
using CacheScope.Shared;
using CacheScope.Shared.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CacheScope.Tests;

public class ProductCacheServiceTests
{
    private sealed record Harness(
        ProductCacheService Service,
        FakeMemoryLayer Memory,
        FakeRedisLayer Redis,
        FakeProductStore Store,
        CachePolicyState Policy);

    private static Harness Build()
    {
        var memory = new FakeMemoryLayer();
        var redis = new FakeRedisLayer();
        var store = new FakeProductStore();
        var metrics = new FakeDbMetrics();
        var policy = new CachePolicyState();
        var refreshAhead = new RefreshAheadScheduler(new StubScopeFactory(), memory, redis, NullLogger<RefreshAheadScheduler>.Instance);
        var service = new ProductCacheService(
            memory, redis, store, metrics, policy,
            new WriteBehindQueue(), refreshAhead, new SingleFlight(),
            new ActivitySource("test"), NullLogger<ProductCacheService>.Instance);
        return new Harness(service, memory, redis, store, policy);
    }

    [Fact]
    public async Task Cold_read_falls_through_to_database_then_serves_from_memory()
    {
        var h = Build();

        var first = await h.Service.GetAsync(1);
        Assert.Equal(CacheLayer.Database, first.ServedBy);
        Assert.Equal(1, h.Store.QueryCount);

        var second = await h.Service.GetAsync(1);
        Assert.Equal(CacheLayer.Memory, second.ServedBy);
        Assert.Equal(1, h.Store.QueryCount); // no additional DB query
    }

    [Fact]
    public async Task Redis_serves_when_memory_is_cold_but_redis_is_warm()
    {
        var h = Build();
        await h.Service.GetAsync(1);   // populates memory + redis
        h.Memory.Clear();              // evict L2 only

        var result = await h.Service.GetAsync(1);
        Assert.Equal(CacheLayer.Redis, result.ServedBy);
    }

    [Fact]
    public async Task Read_captures_a_per_layer_waterfall()
    {
        var h = Build();
        var result = await h.Service.GetAsync(1); // cold: memory miss -> redis miss -> db hit
        Assert.Collection(result.Segments,
            s => Assert.Equal(CacheLayer.Memory, s.Layer),
            s => Assert.Equal(CacheLayer.Redis, s.Layer),
            s => Assert.Equal(CacheLayer.Database, s.Layer));
    }

    [Fact]
    public async Task CacheAside_write_invalidates_so_next_read_hits_database()
    {
        var h = Build();
        h.Policy.Apply(new CachePolicyUpdate { WriteStrategy = WriteStrategy.CacheAside });
        await h.Service.GetAsync(1);                 // warm caches
        var queriesBefore = h.Store.QueryCount;

        await h.Service.UpdateAsync(1, p => p.Price = 999);
        var afterWrite = await h.Service.GetAsync(1);

        Assert.Equal(CacheLayer.Database, afterWrite.ServedBy); // caches were invalidated
        Assert.True(h.Store.QueryCount > queriesBefore);
    }

    [Fact]
    public async Task WriteThrough_updates_caches_so_next_read_hits_memory_with_new_value()
    {
        var h = Build();
        h.Policy.Apply(new CachePolicyUpdate { WriteStrategy = WriteStrategy.WriteThrough });
        await h.Service.GetAsync(1); // warm

        await h.Service.UpdateAsync(1, p => p.Price = 777);
        var afterWrite = await h.Service.GetAsync(1);

        Assert.Equal(CacheLayer.Memory, afterWrite.ServedBy); // written through, not invalidated
        Assert.Equal(777, afterWrite.Value!.Price);
    }
}
