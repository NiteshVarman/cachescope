using System.Collections.Concurrent;
using CacheScope.Shared.Tracing;

namespace CacheScope.Analytics;

/// <summary>
/// Keeps the most recent request details in a bounded ring, indexed by correlation id
/// for on-demand lookup from the live stream.
/// </summary>
public sealed class RequestDetailStore : IRequestDetailStore
{
    private const int Capacity = 500;
    private readonly ConcurrentQueue<RequestDetail> _recent = new();
    private readonly ConcurrentDictionary<string, RequestDetail> _byCorrelation = new();

    public void Record(RequestDetail detail)
    {
        _recent.Enqueue(detail);
        _byCorrelation[detail.CorrelationId] = detail;

        while (_recent.Count > Capacity && _recent.TryDequeue(out var evicted))
        {
            // Only drop the index entry if it still points at the evicted detail.
            if (_byCorrelation.TryGetValue(evicted.CorrelationId, out var current) && ReferenceEquals(current, evicted))
            {
                _byCorrelation.TryRemove(evicted.CorrelationId, out _);
            }
        }
    }

    public RequestDetail? Get(string correlationId) =>
        _byCorrelation.TryGetValue(correlationId, out var d) ? d : null;

    public IReadOnlyList<RequestDetail> Recent(int max = 100) =>
        _recent.Reverse().Take(max).ToArray();
}
