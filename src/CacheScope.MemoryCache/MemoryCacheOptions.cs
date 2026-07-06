namespace CacheScope.MemoryCache;

public sealed class MemoryCacheLayerOptions
{
    public const string SectionName = "MemoryCache";

    /// <summary>Absolute time-to-live for L2 entries. Runtime-configurable in Phase 5.</summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Optional sliding expiration; when set, an entry's life renews on access.</summary>
    public TimeSpan? SlidingExpiration { get; set; }
}
