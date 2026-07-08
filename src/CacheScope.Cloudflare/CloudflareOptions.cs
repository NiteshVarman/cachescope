namespace CacheScope.Cloudflare;

public sealed class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    /// <summary>Cloudflare zone id for the site. Empty in local/dev.</summary>
    public string? ZoneId { get; set; }

    /// <summary>API token (Cache Purge for purge; Analytics:Read for edge stats). Empty in local/dev.</summary>
    public string? ApiToken { get; set; }

    /// <summary>Rolling window (minutes) for the edge-stats query.</summary>
    public int AnalyticsWindowMinutes { get; set; } = 60;

    /// <summary>How often to poll Cloudflare for edge stats.</summary>
    public int AnalyticsPollSeconds { get; set; } = 60;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ZoneId) && !string.IsNullOrWhiteSpace(ApiToken);
}
