namespace CacheScope.RedisCache;

public sealed class RedisCacheOptions
{
    public const string SectionName = "RedisCache";

    /// <summary>Absolute time-to-live for L3 entries. Runtime-configurable in Phase 5.</summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Key prefix so CacheScope keys are namespaced within a shared Redis.</summary>
    public string KeyPrefix { get; set; } = "cachescope:";
}
