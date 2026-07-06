namespace CacheScope.TrafficGenerator;

/// <summary>Thread-safe counters for a single traffic run.</summary>
internal sealed class RunMetrics
{
    private long _dispatched;
    private long _completed;
    private long _failed;
    private long _latencySumMicros;
    private long _peakMicros;

    public long Dispatched => Interlocked.Read(ref _dispatched);
    public long Completed => Interlocked.Read(ref _completed);
    public long Failed => Interlocked.Read(ref _failed);
    public long Pending => Math.Max(0, Dispatched - Completed);

    public double AverageLatencyMs
    {
        get
        {
            var c = Completed;
            return c == 0 ? 0 : Interlocked.Read(ref _latencySumMicros) / 1000.0 / c;
        }
    }

    public double PeakLatencyMs => Interlocked.Read(ref _peakMicros) / 1000.0;

    public void Dispatch() => Interlocked.Increment(ref _dispatched);

    public void Complete(double latencyMs, bool failed)
    {
        Interlocked.Increment(ref _completed);
        if (failed)
        {
            Interlocked.Increment(ref _failed);
        }

        var micros = (long)(latencyMs * 1000);
        Interlocked.Add(ref _latencySumMicros, micros);

        long current;
        do
        {
            current = Interlocked.Read(ref _peakMicros);
            if (micros <= current) return;
        } while (Interlocked.CompareExchange(ref _peakMicros, micros, current) != current);
    }
}
