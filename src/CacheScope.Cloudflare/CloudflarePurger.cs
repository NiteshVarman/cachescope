using System.Net.Http.Headers;
using System.Net.Http.Json;
using CacheScope.Shared.Operations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheScope.Cloudflare;

/// <summary>
/// Purges the Cloudflare edge cache via the v4 API. When no zone/token is configured
/// (local dev), it is a safe no-op that explains what would happen in production.
/// </summary>
public sealed class CloudflarePurger(
    HttpClient http,
    IOptions<CloudflareOptions> options,
    ILogger<CloudflarePurger> logger) : ICloudflarePurger
{
    private readonly CloudflareOptions _options = options.Value;

    public async Task<PurgeResult> PurgeAllAsync(CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            return new PurgeResult(false,
                "Cloudflare not configured (no ZoneId/ApiToken). In production this would purge the edge cache.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.cloudflare.com/client/v4/zones/{_options.ZoneId}/purge_cache")
            {
                Content = JsonContent.Create(new { purge_everything = true })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);

            using var response = await http.SendAsync(request, ct);
            var ok = response.IsSuccessStatusCode;
            return new PurgeResult(true, ok
                ? "Cloudflare edge cache purged."
                : $"Cloudflare purge failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cloudflare purge request failed");
            return new PurgeResult(true, $"Cloudflare purge error: {ex.Message}");
        }
    }
}
