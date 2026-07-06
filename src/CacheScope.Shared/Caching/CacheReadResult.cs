using CacheScope.Shared.Tracing;

namespace CacheScope.Shared.Caching;

/// <summary>
/// The outcome of reading a value through the layered cache pipeline: the value
/// itself (if found), the layer that served it, and whether that was a hit or miss.
/// </summary>
public sealed record CacheReadResult<T>
{
    public required T? Value { get; init; }

    /// <summary>The layer that ultimately produced the value.</summary>
    public required CacheLayer ServedBy { get; init; }

    /// <summary>Hit at the serving layer, or Miss when the value did not exist anywhere.</summary>
    public required CacheOutcome Outcome { get; init; }

    /// <summary>Per-layer timing waterfall captured while serving the request.</summary>
    public IReadOnlyList<LayerTiming> Segments { get; init; } = [];

    /// <summary>True when a value was found at any layer.</summary>
    public bool Found => Value is not null;

    public static CacheReadResult<T> Hit(T value, CacheLayer servedBy) =>
        new() { Value = value, ServedBy = servedBy, Outcome = CacheOutcome.Hit };

    /// <summary>Nothing existed at any layer — a genuine miss that fell through to the database.</summary>
    public static CacheReadResult<T> NotFound() =>
        new() { Value = default, ServedBy = CacheLayer.Database, Outcome = CacheOutcome.Miss };
}
