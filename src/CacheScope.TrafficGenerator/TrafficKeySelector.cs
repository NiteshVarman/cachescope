using CacheScope.Shared.Traffic;

namespace CacheScope.TrafficGenerator;

/// <summary>
/// Chooses which product id each generated request targets. The effective strategy is
/// derived from the pattern first (HotKey/Zipf/Random/Bot force a distribution), then
/// falls back to the configured KeySelection.
/// </summary>
public sealed class TrafficKeySelector
{
    private readonly int[] _ids;
    private readonly TrafficConfig _config;
    private readonly Random _rng;
    private readonly double[]? _zipfCumulative;
    private long _sequentialCursor;

    public TrafficKeySelector(IReadOnlyList<int> ids, TrafficConfig config, Random rng)
    {
        _ids = ids.ToArray();
        _config = config;
        _rng = rng;

        if (config.Pattern == TrafficPattern.Zipf && _ids.Length > 0)
        {
            _zipfCumulative = BuildZipf(_ids.Length, 1.0);
        }
    }

    public int Next()
    {
        if (_ids.Length == 0) return 0;

        return _config.Pattern switch
        {
            TrafficPattern.HotKey => _ids[0],
            TrafficPattern.Zipf => _ids[SampleZipf()],
            TrafficPattern.Mixed => SampleMixed(),
            TrafficPattern.BotTraffic => _ids[_rng.Next(_ids.Length)],
            TrafficPattern.CacheStampede => _ids[0], // hammer the single expired hot key
            _ => ByConfiguredSelection()
        };
    }

    private int ByConfiguredSelection()
    {
        switch (EffectiveMode())
        {
            case KeySelectionMode.SingleHotKey:
                return _ids[0];
            case KeySelectionMode.TopNHotKeys:
                var n = Math.Clamp(_config.HotKeyCount, 1, _ids.Length);
                return _ids[_rng.Next(n)];
            case KeySelectionMode.Sequential:
                var idx = (int)(Interlocked.Increment(ref _sequentialCursor) - 1) % _ids.Length;
                return _ids[idx];
            default:
                return _ids[_rng.Next(_ids.Length)];
        }
    }

    private KeySelectionMode EffectiveMode() => _config.KeySelection;

    private int SampleMixed()
    {
        // 80% of traffic to the hot set, 20% to the random tail.
        var n = Math.Clamp(_config.HotKeyCount, 1, _ids.Length);
        return _rng.NextDouble() < 0.8 ? _ids[_rng.Next(n)] : _ids[_rng.Next(_ids.Length)];
    }

    private int SampleZipf()
    {
        var u = _rng.NextDouble();
        var arr = _zipfCumulative!;
        // Binary search for the first cumulative weight >= u.
        var lo = 0;
        var hi = arr.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (arr[mid] < u) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static double[] BuildZipf(int n, double s)
    {
        var cumulative = new double[Math.Max(n, 1)];
        double sum = 0;
        for (var i = 0; i < n; i++)
        {
            sum += 1.0 / Math.Pow(i + 1, s);
            cumulative[i] = sum;
        }
        for (var i = 0; i < n; i++)
        {
            cumulative[i] /= sum; // normalize to [0,1]
        }
        return cumulative;
    }
}
