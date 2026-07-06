using CacheScope.Api.Caching;
using CacheScope.Shared.Models;
using Xunit;

namespace CacheScope.Tests;

public class SingleFlightTests
{
    [Fact]
    public async Task Concurrent_calls_for_same_key_invoke_the_loader_once()
    {
        var sf = new SingleFlight();
        var calls = 0;

        async Task<Product?> Load()
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(50); // hold the flight open so all callers pile up
            return new Product { Id = 1, Name = "P1" };
        }

        var tasks = Enumerable.Range(0, 100).Select(_ => sf.RunAsync("product:1", Load));
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, calls);                       // the stampede collapsed to one load
        Assert.All(results, r => Assert.Equal(1, r!.Id));
    }

    [Fact]
    public async Task Different_keys_load_independently()
    {
        var sf = new SingleFlight();
        var calls = 0;
        Task<Product?> Load() { Interlocked.Increment(ref calls); return Task.FromResult<Product?>(new Product()); }

        await sf.RunAsync("product:1", Load);
        await sf.RunAsync("product:2", Load);

        Assert.Equal(2, calls);
    }
}
