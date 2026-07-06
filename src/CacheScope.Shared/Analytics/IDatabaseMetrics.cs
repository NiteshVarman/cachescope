namespace CacheScope.Shared.Analytics;

/// <summary>
/// Process-wide counters describing how the database (L4) is being used. The story
/// CacheScope tells lives here: queries actually executed vs. queries *prevented*
/// because a cache layer answered first.
/// </summary>
public interface IDatabaseMetrics
{
    long QueriesExecuted { get; }
    long QueriesPrevented { get; }
    double TotalQueryTimeMs { get; }
    double AverageQueryTimeMs { get; }

    /// <summary>Record a real database round-trip and how long it took.</summary>
    void RecordQuery(double elapsedMs);

    /// <summary>Record that a cache layer served the request, sparing the database a query.</summary>
    void RecordPrevented();

    void Reset();
}
