namespace CacheScope.Shared.Operations;

/// <summary>Outcome of one stampede scenario (with or without single-flight protection).</summary>
public sealed record StampedeScenario
{
    public required bool ProtectionEnabled { get; init; }
    public required int Concurrency { get; init; }
    public required long DatabaseQueries { get; init; }
    public required double DurationMs { get; init; }
}

/// <summary>Side-by-side comparison: the same hot-key stampede, unprotected vs single-flight.</summary>
public sealed record StampedeResult
{
    public required int HotKeyId { get; init; }
    public required StampedeScenario Unprotected { get; init; }
    public required StampedeScenario Protected { get; init; }
}
