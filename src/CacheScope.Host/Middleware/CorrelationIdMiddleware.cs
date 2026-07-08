using System.Diagnostics;
using CacheScope.Shared.Tracing;

namespace CacheScope.Host.Middleware;

/// <summary>
/// Ensures every request carries a correlation id: reads an inbound X-Correlation-Id
/// (or Cloudflare's Ray id) if present, otherwise mints one. The id is attached to the
/// current Activity, pushed into the log scope, echoed back on the response, and stashed
/// in HttpContext.Items for downstream handlers.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.Items[CorrelationConstants.CorrelationIdItemKey] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);

        // Echo the id back before the response starts flushing.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationConstants.CorrelationIdHeader] = correlationId;
            // Expose Resource Timing details (transferSize, etc.) to the cross-origin dashboard
            // so it can measure L1 browser-cache hits (transferSize == 0 ⇒ served from cache).
            context.Response.Headers["Timing-Allow-Origin"] = "*";
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object>
               {
                   [CorrelationConstants.CorrelationIdItemKey] = correlationId
               }))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationConstants.CorrelationIdHeader, out var incoming)
            && !string.IsNullOrWhiteSpace(incoming))
        {
            return incoming.ToString();
        }

        // Cloudflare stamps every edge request with a CF-Ray id — reuse it when present
        // so the id ties back to Cloudflare's own logs.
        if (context.Request.Headers.TryGetValue("CF-Ray", out var cfRay)
            && !string.IsNullOrWhiteSpace(cfRay))
        {
            return cfRay.ToString();
        }

        return Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("n");
    }
}
