using CacheScope.Shared;
using CacheScope.Shared.Analytics;
using CacheScope.Shared.Tracing;

namespace CacheScope.Analytics;

/// <summary>
/// Lock-free rolling aggregate over all request traces. Latency percentiles (P95/P99)
/// arrive in Phase 4; this Phase 2 version tracks counts, average and peak latency.
/// </summary>
public sealed class LiveStats : ILiveStats
{
    private long _total;
    private long _cloudflare, _browser, _memory, _redis, _database;
    private long _failed;
    private long _latencySumMicros; // sum of latencies in microseconds for exact Interlocked math
    private long _peakMicros;

    public void Record(RequestTrace trace)
    {
        Interlocked.Increment(ref _total);

        switch (trace.ServedBy)
        {
            case CacheLayer.Cloudflare: Interlocked.Increment(ref _cloudflare); break;
            case CacheLayer.Browser: Interlocked.Increment(ref _browser); break;
            case CacheLayer.Memory: Interlocked.Increment(ref _memory); break;
            case CacheLayer.Redis: Interlocked.Increment(ref _redis); break;
            case CacheLayer.Database: Interlocked.Increment(ref _database); break;
        }

        if (trace.StatusCode >= 500)
        {
            Interlocked.Increment(ref _failed);
        }

        var micros = (long)(trace.ResponseTimeMs * 1000);
        Interlocked.Add(ref _latencySumMicros, micros);
        UpdatePeak(micros);
    }

    private void UpdatePeak(long micros)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref _peakMicros);
            if (micros <= current)
            {
                return;
            }
        } while (Interlocked.CompareExchange(ref _peakMicros, micros, current) != current);
    }

    public LiveStatsSnapshot Snapshot()
    {
        var total = Interlocked.Read(ref _total);
        var cloudflare = Interlocked.Read(ref _cloudflare);
        var browser = Interlocked.Read(ref _browser);
        var memory = Interlocked.Read(ref _memory);
        var redis = Interlocked.Read(ref _redis);
        var database = Interlocked.Read(ref _database);
        var latencySum = Interlocked.Read(ref _latencySumMicros) / 1000.0;

        var cacheHits = cloudflare + browser + memory + redis;

        return new LiveStatsSnapshot
        {
            TotalRequests = total,
            CloudflareHits = cloudflare,
            BrowserHits = browser,
            MemoryHits = memory,
            RedisHits = redis,
            DatabaseHits = database,
            FailedRequests = Interlocked.Read(ref _failed),
            CacheHitRatio = total == 0 ? 0 : (double)cacheHits / total,
            AverageLatencyMs = total == 0 ? 0 : latencySum / total,
            PeakLatencyMs = Interlocked.Read(ref _peakMicros) / 1000.0
        };
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _total, 0);
        Interlocked.Exchange(ref _cloudflare, 0);
        Interlocked.Exchange(ref _browser, 0);
        Interlocked.Exchange(ref _memory, 0);
        Interlocked.Exchange(ref _redis, 0);
        Interlocked.Exchange(ref _database, 0);
        Interlocked.Exchange(ref _failed, 0);
        Interlocked.Exchange(ref _latencySumMicros, 0);
        Interlocked.Exchange(ref _peakMicros, 0);
    }
}
