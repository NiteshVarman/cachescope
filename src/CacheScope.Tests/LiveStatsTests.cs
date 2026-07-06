using CacheScope.Analytics;
using CacheScope.Shared;
using CacheScope.Shared.Tracing;
using Xunit;

namespace CacheScope.Tests;

public class LiveStatsTests
{
    private static RequestTrace Trace(CacheLayer layer, double ms = 5, int status = 200) => new()
    {
        RequestId = 1, CorrelationId = "c", Timestamp = DateTimeOffset.UnixEpoch,
        Method = "GET", Path = "/p", ServedBy = layer, Outcome = CacheOutcome.Hit,
        ResponseTimeMs = ms, StatusCode = status
    };

    [Fact]
    public void Counts_and_hit_ratio_reflect_recorded_traces()
    {
        var stats = new LiveStats();
        stats.Record(Trace(CacheLayer.Memory));
        stats.Record(Trace(CacheLayer.Memory));
        stats.Record(Trace(CacheLayer.Redis));
        stats.Record(Trace(CacheLayer.Database));

        var s = stats.Snapshot();
        Assert.Equal(4, s.TotalRequests);
        Assert.Equal(2, s.MemoryHits);
        Assert.Equal(1, s.RedisHits);
        Assert.Equal(1, s.DatabaseHits);
        // 3 of 4 served by a cache layer.
        Assert.Equal(0.75, s.CacheHitRatio, 3);
    }

    [Fact]
    public void Tracks_peak_and_failures_and_resets()
    {
        var stats = new LiveStats();
        stats.Record(Trace(CacheLayer.Database, ms: 100, status: 500));
        stats.Record(Trace(CacheLayer.Memory, ms: 2));

        var s = stats.Snapshot();
        Assert.Equal(1, s.FailedRequests);
        Assert.True(s.PeakLatencyMs >= 100);

        stats.Reset();
        Assert.Equal(0, stats.Snapshot().TotalRequests);
    }
}
