using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CacheScope.Shared.Analytics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheScope.Cloudflare;

/// <summary>
/// Queries Cloudflare's GraphQL Analytics API for the zone's requests grouped by
/// cacheStatus — the only way to observe L0 edge hits, which never reach the origin.
/// </summary>
public sealed class CloudflareEdgeStatsClient(
    HttpClient http,
    IOptions<CloudflareOptions> options,
    ILogger<CloudflareEdgeStatsClient> logger)
{
    private readonly CloudflareOptions _options = options.Value;

    private const string Query =
        "query($zone:String!,$since:Time!,$until:Time!){viewer{zones(filter:{zoneTag:$zone})" +
        "{httpRequestsAdaptiveGroups(limit:100,filter:{datetime_geq:$since,datetime_leq:$until})" +
        "{count dimensions{cacheStatus}}}}}";

    public async Task<EdgeStatsSnapshot> QueryAsync(CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            return EdgeStatsSnapshot.NotConfigured();
        }

        var window = Math.Max(5, _options.AnalyticsWindowMinutes);
        var until = DateTimeOffset.UtcNow;
        var since = until.AddMinutes(-window);

        var body = JsonSerializer.Serialize(new
        {
            query = Query,
            variables = new
            {
                zone = _options.ZoneId,
                since = since.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                until = until.ToString("yyyy-MM-ddTHH:mm:ssZ")
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.cloudflare.com/client/v4/graphql")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);

        try
        {
            using var response = await http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Cloudflare analytics query failed: {Status} {Body}", (int)response.StatusCode, json);
                return new EdgeStatsSnapshot { Configured = true, WindowMinutes = window, Message = $"Query failed ({(int)response.StatusCode})." };
            }
            return Parse(json, window);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cloudflare analytics query threw");
            return new EdgeStatsSnapshot { Configured = true, WindowMinutes = window, Message = "Query error." };
        }
    }

    private static EdgeStatsSnapshot Parse(string json, int window)
    {
        using var doc = JsonDocument.Parse(json);
        long hit = 0, miss = 0, expired = 0, revalidated = 0, dynamic = 0, none = 0;

        var groups = doc.RootElement
            .GetProperty("data").GetProperty("viewer").GetProperty("zones")[0]
            .GetProperty("httpRequestsAdaptiveGroups");

        foreach (var g in groups.EnumerateArray())
        {
            var count = g.GetProperty("count").GetInt64();
            var status = g.GetProperty("dimensions").GetProperty("cacheStatus").GetString();
            switch (status)
            {
                case "hit": hit = count; break;
                case "miss": miss = count; break;
                case "expired": expired = count; break;
                case "revalidated": revalidated = count; break;
                case "dynamic": dynamic = count; break;
                case "none": none = count; break;
            }
        }

        var cacheable = hit + miss + expired + revalidated;
        return new EdgeStatsSnapshot
        {
            Configured = true,
            Hit = hit, Miss = miss, Expired = expired, Revalidated = revalidated, Dynamic = dynamic, None = none,
            Total = hit + miss + expired + revalidated + dynamic + none,
            EdgeHitRatio = cacheable == 0 ? 0 : (double)hit / cacheable,
            WindowMinutes = window,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
