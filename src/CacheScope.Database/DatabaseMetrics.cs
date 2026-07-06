using CacheScope.Shared.Analytics;

namespace CacheScope.Database;

/// <summary>Thread-safe, in-memory implementation of <see cref="IDatabaseMetrics"/>.</summary>
public sealed class DatabaseMetrics : IDatabaseMetrics
{
    private long _executed;
    private long _prevented;
    private long _totalQueryTimeTicks; // store microseconds*10 as long for Interlocked

    public long QueriesExecuted => Interlocked.Read(ref _executed);
    public long QueriesPrevented => Interlocked.Read(ref _prevented);

    public double TotalQueryTimeMs => Interlocked.Read(ref _totalQueryTimeTicks) / 1000.0;

    public double AverageQueryTimeMs
    {
        get
        {
            var executed = QueriesExecuted;
            return executed == 0 ? 0 : TotalQueryTimeMs / executed;
        }
    }

    public void RecordQuery(double elapsedMs)
    {
        Interlocked.Increment(ref _executed);
        // Accumulate as integer microseconds to keep Interlocked arithmetic exact.
        Interlocked.Add(ref _totalQueryTimeTicks, (long)(elapsedMs * 1000));
    }

    public void RecordPrevented() => Interlocked.Increment(ref _prevented);

    public void Reset()
    {
        Interlocked.Exchange(ref _executed, 0);
        Interlocked.Exchange(ref _prevented, 0);
        Interlocked.Exchange(ref _totalQueryTimeTicks, 0);
    }
}
