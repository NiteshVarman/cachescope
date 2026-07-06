using CacheScope.Shared.Tracing;

namespace CacheScope.Shared.Analytics;

/// <summary>Process-wide rolling aggregate of request traces. Thread-safe.</summary>
public interface ILiveStats
{
    void Record(RequestTrace trace);
    LiveStatsSnapshot Snapshot();
    void Reset();
}
