using CacheScope.Shared.Traffic;
using CacheScope.TrafficGenerator;
using Xunit;

namespace CacheScope.Tests;

public class TrafficKeySelectorTests
{
    private static readonly int[] Universe = Enumerable.Range(1, 100).ToArray();

    [Fact]
    public void HotKey_pattern_always_targets_the_first_key()
    {
        var sel = new TrafficKeySelector(Universe, new TrafficConfig { Pattern = TrafficPattern.HotKey }, new Random(1));
        for (var i = 0; i < 50; i++) Assert.Equal(Universe[0], sel.Next());
    }

    [Fact]
    public void Sequential_selection_cycles_through_the_universe()
    {
        var cfg = new TrafficConfig { Pattern = TrafficPattern.Steady, KeySelection = KeySelectionMode.Sequential };
        var sel = new TrafficKeySelector(Universe, cfg, new Random(1));
        Assert.Equal(Universe[0], sel.Next());
        Assert.Equal(Universe[1], sel.Next());
        Assert.Equal(Universe[2], sel.Next());
    }

    [Fact]
    public void Zipf_skews_heavily_toward_the_hot_keys()
    {
        var sel = new TrafficKeySelector(Universe, new TrafficConfig { Pattern = TrafficPattern.Zipf }, new Random(42));
        var hot = 0;
        var cold = 0;
        for (var i = 0; i < 10_000; i++)
        {
            var id = sel.Next();
            if (id <= 5) hot++;          // first 5 keys
            if (id >= 96) cold++;        // last 5 keys
        }
        Assert.True(hot > cold * 5, $"zipf should favour hot keys: hot={hot} cold={cold}");
    }

    [Fact]
    public void Random_selection_stays_within_the_universe()
    {
        var cfg = new TrafficConfig { Pattern = TrafficPattern.RandomKeys, KeySelection = KeySelectionMode.Random };
        var sel = new TrafficKeySelector(Universe, cfg, new Random(7));
        for (var i = 0; i < 200; i++)
        {
            var id = sel.Next();
            Assert.InRange(id, 1, 100);
        }
    }
}
