namespace CacheScope.Shared.Operations;

public sealed record PurgeResult(bool Attempted, string Message);

/// <summary>Purges the Cloudflare edge cache (L0). No-op with a message when not configured.</summary>
public interface ICloudflarePurger
{
    Task<PurgeResult> PurgeAllAsync(CancellationToken ct = default);
}
