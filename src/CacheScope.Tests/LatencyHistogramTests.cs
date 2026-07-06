using CacheScope.Analytics;
using Xunit;

namespace CacheScope.Tests;

public class LatencyHistogramTests
{
    [Fact]
    public void Empty_histogram_returns_zero()
    {
        var h = new LatencyHistogram();
        Assert.Equal(0, h.Percentile(0.5));
        Assert.Equal(0, h.Percentile(0.99));
    }

    [Fact]
    public void Percentiles_fall_in_the_containing_bucket()
    {
        var h = new LatencyHistogram();
        for (var i = 0; i < 1000; i++) h.Record(50); // all 50ms -> bucket (32,64]

        var p50 = h.Percentile(0.5);
        Assert.InRange(p50, 32, 64);
    }

    [Fact]
    public void Percentiles_are_monotonic_for_a_spread()
    {
        var h = new LatencyHistogram();
        for (var i = 0; i < 900; i++) h.Record(2);    // fast bulk
        for (var i = 0; i < 100; i++) h.Record(1000); // slow tail

        var p50 = h.Percentile(0.5);
        var p99 = h.Percentile(0.99);

        Assert.True(p50 < p99, $"expected p50({p50}) < p99({p99})");
        Assert.True(p50 <= 8, "median should reflect the fast bulk");
        Assert.True(p99 >= 512, "p99 should reflect the slow tail");
    }
}
