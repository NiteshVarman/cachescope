namespace CacheScope.Shared;

/// <summary>
/// The layers a request can traverse, in order of proximity to the client.
/// A request is "served by" the first layer that satisfies it.
/// </summary>
public enum CacheLayer
{
    /// <summary>L0 — Cloudflare edge cache. Never reaches origin; observed via CF-Cache-Status.</summary>
    Cloudflare = 0,

    /// <summary>L1 — Browser cache. Never reaches the network; observed via response headers.</summary>
    Browser = 1,

    /// <summary>L2 — In-process application memory cache (IMemoryCache).</summary>
    Memory = 2,

    /// <summary>L3 — Distributed Redis cache.</summary>
    Redis = 3,

    /// <summary>L4 — embedded SQLite database (the source of truth / fallback).</summary>
    Database = 4
}
