using System.Collections.Concurrent;
using CacheScope.Shared.Analytics;

namespace CacheScope.Analytics;

/// <summary>Thread-safe rolling window of the most recent per-second points.</summary>
public sealed class MetricsTimeline : IMetricsTimeline
{
    private const int Capacity = 120; // ~2 minutes of history
    private readonly ConcurrentQueue<MetricsTimelinePoint> _points = new();

    public void Add(MetricsTimelinePoint point)
    {
        _points.Enqueue(point);
        while (_points.Count > Capacity && _points.TryDequeue(out _)) { }
    }

    public IReadOnlyList<MetricsTimelinePoint> Recent() => _points.ToArray();

    public void Reset()
    {
        while (_points.TryDequeue(out _)) { }
    }
}
