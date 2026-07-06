namespace CacheScope.Analytics;

/// <summary>
/// A fixed-bucket streaming histogram of latencies. Records are O(1) Interlocked
/// increments (no per-tick sorting), and percentiles are estimated by walking the
/// cumulative counts and interpolating within the containing bucket.
/// </summary>
public sealed class LatencyHistogram
{
    // Upper bounds in milliseconds; the final bucket catches everything above.
    private static readonly double[] Bounds =
        [1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, double.PositiveInfinity];

    private readonly long[] _counts = new long[Bounds.Length];

    public void Record(double ms)
    {
        var idx = Array.FindIndex(Bounds, b => ms <= b);
        if (idx < 0) idx = Bounds.Length - 1;
        Interlocked.Increment(ref _counts[idx]);
    }

    public double Percentile(double p)
    {
        var counts = new long[_counts.Length];
        long total = 0;
        for (var i = 0; i < counts.Length; i++)
        {
            counts[i] = Interlocked.Read(ref _counts[i]);
            total += counts[i];
        }
        if (total == 0) return 0;

        var target = p * total;
        long cumulative = 0;
        for (var i = 0; i < counts.Length; i++)
        {
            var prev = cumulative;
            cumulative += counts[i];
            if (cumulative < target) continue;

            var lower = i == 0 ? 0 : Bounds[i - 1];
            var upper = double.IsPositiveInfinity(Bounds[i]) ? lower * 2 : Bounds[i];
            // Linear interpolation of the target position within this bucket.
            var withinFraction = counts[i] == 0 ? 0 : (target - prev) / counts[i];
            return lower + (upper - lower) * withinFraction;
        }
        return Bounds[^2];
    }

    public void Reset()
    {
        for (var i = 0; i < _counts.Length; i++)
        {
            Interlocked.Exchange(ref _counts[i], 0);
        }
    }
}
