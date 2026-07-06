namespace CacheScope.Shared.Tracing;

/// <summary>Well-known names used to propagate correlation across the request pipeline.</summary>
public static class CorrelationConstants
{
    /// <summary>Response/request header carrying the correlation id end to end.</summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    /// <summary>Cloudflare's cache verdict header (HIT / MISS / BYPASS / EXPIRED / DYNAMIC).</summary>
    public const string CfCacheStatusHeader = "CF-Cache-Status";

    /// <summary>Response header advertising which internal layer served the request.</summary>
    public const string ServedByHeader = "X-Served-By";

    /// <summary>The OpenTelemetry / logging scope key for the correlation id.</summary>
    public const string CorrelationIdItemKey = "CorrelationId";

    /// <summary>ActivitySource / service name used for OpenTelemetry tracing.</summary>
    public const string ActivitySourceName = "CacheScope";
}
