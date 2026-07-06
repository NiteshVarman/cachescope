namespace CacheScope.Shared.Traffic;

/// <summary>The workload shape to simulate.</summary>
public enum TrafficPattern
{
    ColdStart,      // flush all caches first — everything starts at the database
    WarmCache,      // pre-warm caches (and resume serverless SQL) before load
    Steady,         // constant rate
    Burst,          // alternating spikes and lulls
    HotKey,         // every request targets a single key
    RandomKeys,     // uniform random keys
    Zipf,           // zipfian — a few keys get most of the traffic
    CacheStampede,  // expire a hot key, then hammer it concurrently
    BotTraffic,     // high-concurrency scan across random keys
    Mixed           // blend of hot keys and random tail
}

/// <summary>How keys are chosen for each request.</summary>
public enum KeySelectionMode
{
    SingleHotKey,
    TopNHotKeys,
    Random,
    Sequential
}

/// <summary>
/// Which path the traffic exercises. Origin runs server-side and reaches L2–L4 only.
/// Edge is browser-driven (through Cloudflare) and is the only way to hit L0/L1 — the
/// server can't generate Edge traffic without bypassing the CDN.
/// </summary>
public enum TrafficMode
{
    Origin,
    Edge
}
