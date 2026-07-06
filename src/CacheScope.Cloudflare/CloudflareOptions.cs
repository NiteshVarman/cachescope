namespace CacheScope.Cloudflare;

public sealed class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    /// <summary>Cloudflare zone id for the site. Empty in local/dev.</summary>
    public string? ZoneId { get; set; }

    /// <summary>API token with Cache Purge permission. Empty in local/dev.</summary>
    public string? ApiToken { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ZoneId) && !string.IsNullOrWhiteSpace(ApiToken);
}
