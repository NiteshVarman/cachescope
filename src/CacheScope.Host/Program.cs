using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using CacheScope.Analytics;
using CacheScope.Api;
using CacheScope.Api.Endpoints;
using CacheScope.Cloudflare;
using CacheScope.Database;
using CacheScope.Host.HealthChecks;
using CacheScope.Host.Middleware;
using CacheScope.Realtime;
using CacheScope.Shared.Tracing;
using CacheScope.TrafficGenerator;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Service name / version used across telemetry.
// ---------------------------------------------------------------------------
const string serviceName = CorrelationConstants.ActivitySourceName;
var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";

// ---------------------------------------------------------------------------
// OpenTelemetry. If an Application Insights connection string is configured we
// export straight to Azure Monitor; otherwise we still run OTel locally (traces
// are available in-process and can be pointed at an OTLP collector) so the app
// works offline during development.
// ---------------------------------------------------------------------------
var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName, serviceVersion: serviceVersion))
    .WithTracing(t => t
        .AddSource(serviceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation());

var appInsightsConnection = builder.Configuration["ApplicationInsights:ConnectionString"]
    ?? builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnection))
{
    otel.UseAzureMonitor(o => o.ConnectionString = appInsightsConnection);
}

// ---------------------------------------------------------------------------
// The cache pipeline (L2/L3/L4 layers + cache-aside orchestrator + trace infra),
// rolling analytics, and the SignalR realtime backbone.
// ---------------------------------------------------------------------------
builder.Services.AddCacheScopePipeline(builder.Configuration);
builder.Services.AddAnalytics();
builder.Services.AddRealtime(builder.Configuration);
builder.Services.AddTrafficGenerator();
builder.Services.AddCloudflare(builder.Configuration);

// ---------------------------------------------------------------------------
// Health checks: liveness (process up) + readiness (Redis + SQL reachable).
// ---------------------------------------------------------------------------
builder.Services.AddHealthChecks()
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"])
    .AddCheck<SqlHealthCheck>("sql", tags: ["ready"]);

builder.Services.AddOpenApi();

// Serialize enums as string names across all HTTP JSON responses too, matching
// the SignalR payloads and the TypeScript client contract.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// Shared ActivitySource so any layer can open a span under the CacheScope trace.
builder.Services.AddSingleton(new ActivitySource(serviceName, serviceVersion));

// CORS for the Angular dev server (Phase 2+). In Development we allow any localhost
// origin so the dev server / preview tooling can run on any port; SignalR needs
// AllowCredentials, which forbids a wildcard origin, hence SetIsOriginAllowed.
const string devCors = "cachescope-dev";
builder.Services.AddCors(o => o.AddPolicy(devCors, p => p
    .SetIsOriginAllowed(origin => new Uri(origin).Host is "localhost" or "127.0.0.1")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(devCors);
}

app.UseMiddleware<CorrelationIdMiddleware>();

// Apply EF migrations at startup (dev + first cloud boot). Against a paused
// serverless DB this triggers a resume; guarded so a DB outage doesn't crash boot.
await ApplyMigrationsAsync(app);

// Liveness: is the process up? Readiness: are Redis + SQL reachable?
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// The product cache pipeline endpoints, traffic generator, and the SignalR hub.
app.MapProductEndpoints();
app.MapTrafficEndpoints();
app.MapAnalyticsEndpoints();
app.MapCacheOpsEndpoints();
app.MapStampedeEndpoints();
app.MapObservabilityEndpoints();
app.MapRealtime();

// A Phase 0 diagnostic endpoint proving the full path is wired: it returns the
// correlation id, the observed Cloudflare cache status, and the current trace id.
app.MapGet("/diagnostics/echo", (HttpContext ctx) =>
{
    var correlationId = ctx.Items[CorrelationConstants.CorrelationIdItemKey] as string;
    ctx.Response.Headers[CorrelationConstants.ServedByHeader] = nameof(CacheScope.Shared.CacheLayer.Database);

    return Results.Ok(new
    {
        message = "CacheScope Host is alive.",
        correlationId,
        traceId = Activity.Current?.TraceId.ToString(),
        cfCacheStatus = ctx.Request.Headers[CorrelationConstants.CfCacheStatusHeader].ToString(),
        serverTimeUtc = DateTimeOffset.UtcNow
    });
})
.WithName("DiagnosticsEcho");

app.Run();

static async Task ApplyMigrationsAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CacheScopeDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration failed at startup; the app will still start.");
    }
}

// Exposed for WebApplicationFactory-based integration tests in later phases.
public partial class Program;
