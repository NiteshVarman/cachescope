using CacheScope.Shared.Caching;
using Xunit;

namespace CacheScope.Tests;

public class CachePolicyStateTests
{
    [Fact]
    public void Defaults_to_cache_aside()
    {
        var p = new CachePolicyState();
        Assert.Equal(WriteStrategy.CacheAside, p.WriteStrategy);
        Assert.False(p.StampedeProtection);
    }

    [Fact]
    public void Apply_only_changes_provided_fields()
    {
        var p = new CachePolicyState();
        var before = p.Snapshot();

        p.Apply(new CachePolicyUpdate { WriteStrategy = WriteStrategy.WriteThrough, MemoryTtlSeconds = 15 });

        var after = p.Snapshot();
        Assert.Equal(WriteStrategy.WriteThrough, after.WriteStrategy);
        Assert.Equal(15, after.MemoryTtlSeconds);
        // Untouched fields retain their prior values.
        Assert.Equal(before.RedisTtlSeconds, after.RedisTtlSeconds);
        Assert.Equal(before.MemoryExpiration, after.MemoryExpiration);
    }

    [Fact]
    public void Non_positive_ttls_are_ignored()
    {
        var p = new CachePolicyState();
        var original = p.Snapshot().MemoryTtlSeconds;
        p.Apply(new CachePolicyUpdate { MemoryTtlSeconds = 0 });
        Assert.Equal(original, p.Snapshot().MemoryTtlSeconds);
    }
}
