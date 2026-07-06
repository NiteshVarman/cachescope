namespace CacheScope.Database;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Optional artificial latency added to every query, in milliseconds. Real queries
    /// against a warm local SQL are sub-millisecond, which hides the value of caching.
    /// Dialing this up simulates a heavy aggregation/join so the L2/L3 vs L4 latency gap
    /// is visible in the demo. Default 0 = pure measured latency.
    /// </summary>
    public int SimulatedQueryLatencyMs { get; set; }
}
