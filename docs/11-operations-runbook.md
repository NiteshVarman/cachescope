# 11 · Operations Runbook

← [10 · Deployment](10-deployment.md) · [Wiki home](README.md) · Next → [12 · Glossary](12-glossary.md)

---

How to operate, debug, and safely change CacheScope. Includes the **real production issues** we hit
and how they were fixed — these are the most valuable pages for a new engineer (and great interview
material).

## 11.1 Health & where to look

- **Liveness:** `GET /health` — is the process up?
- **Readiness:** `GET /health/ready` — are Redis **and** SQL reachable? ([`RedisHealthCheck`](../src/CacheScope.Host/HealthChecks/RedisHealthCheck.cs),
  [`SqlHealthCheck`](../src/CacheScope.Host/HealthChecks/SqlHealthCheck.cs)).
- **Diagnostics probe:** `GET /diagnostics/echo` — returns the correlation id + trace id (confirms
  the pipeline is wired).
- **Container logs:** `az containerapp logs show -n cachescope-api -g cachescope-rg --type console --tail 100`.
- **Traces/metrics/logs:** Azure **Application Insights** → Transaction search / Failures /
  Performance. Search by `operation_Id` (= the OpenTelemetry trace id) to see a request's per-layer
  spans.
- **Per-request forensics in-app:** click a row in the Live Request Stream, or
  `GET /api/traces/{correlationId}`.
- **L0 edge:** the Cloudflare dashboard analytics, or the app's edge card (`GET /api/analytics` →
  `cloudflareEdge`).

## 11.2 Real production issues we hit (and the fix)

### (a) First-boot schema failed silently → `Invalid object name 'Products'`
- **Symptom:** after the very first cloud deploy (back when L4 was managed Azure SQL), `/health` was
  200 but `/api/products/{id}` returned 500 with `Invalid object name 'Products'`.
- **Cause:** the serverless SQL database was **cold/paused** when the app booted; the startup schema
  step threw; the startup code *catches and continues* (so a DB blip doesn't crash boot), so the
  schema was never created.
- **Fix at the time:** with SQL warm, roll a fresh revision so startup re-ran against a ready DB.
- **Permanent fix:** L4 was moved to **embedded SQLite** (a file inside the API container). The
  schema is now built in-process with `EnsureCreatedAsync()` against a local file that is always
  "warm," so there is no external DB to be cold/paused and this failure mode is gone entirely.
- **Lesson:** a startup schema step that depends on a scale-to-zero/serverless external DB needs a
  retry or warm-up; removing the external dependency removes the failure mode outright.

### (b) Multi-replica state fragmentation
- **Symptom:** started a 600-request traffic run, but `/api/traffic/status` reported a *different*
  run with different numbers; layer counts looked inconsistent.
- **Cause:** under load, Container Apps had **scaled to 2 replicas**. CacheScope's state (stats,
  traffic-run status, SignalR) is **in-process singletons**, so each replica had its own; requests
  hit different replicas.
- **Fix:** pin `maxReplicas: 1` (min 0 keeps scale-to-zero). For an observability tool, coherent
  numbers beat horizontal scale.
- **Lesson:** in-process state doesn't survive horizontal scale. A scaled version needs shared state
  (Azure SignalR Service + a Redis-backed store). Know this trade-off.

### (c) CORS blocked the dashboard
- **Symptom:** the UI loaded but showed "Disconnected" and all zeros.
- **Cause:** the browser blocked API/SignalR calls because the viewing **origin** wasn't in the API's
  CORS allow-list (e.g. viewing on the `*.workers.dev` URL, which wasn't allowed).
- **Fix:** add the origin to `Cors:AllowedOrigins` (and it's set live as an env var). SignalR needs
  `AllowCredentials`, which forbids a wildcard origin — hence the explicit allow-list.

### (d) L1 probe always 0 → cross-origin timing was opaque
- **Symptom:** "Measure L1" always returned 0; the diagnostic showed `transferSize=0,
  decodedBodySize=0`.
- **Cause 1:** cross-origin Resource Timing is **opaque** unless the API sends `Timing-Allow-Origin`.
  Fixed by adding that header in `CorrelationIdMiddleware`.
- **Cause 2:** Cloudflare's edge was serving **stale copies cached before** the header existed
  (`CF-Cache-Status: HIT/REVALIDATED` replays old headers). Fixed by **purging the edge cache** once
  (origin always sends the header now, so refreshed copies carry it).
- **Lesson:** headers you add only take effect on the origin path until the edge cache is refreshed;
  purge after header changes.

### (e) Container app cold start (scale-to-zero)
- **Symptom:** the first request after a long idle is slow (a few seconds).
- **Cause:** the container app **scaled to zero** while idle and must spin a replica back up (and
  rebuild the in-process SQLite DB on boot) before serving the first request.
- **Handling:** expected behaviour; there is no external DB to resume any more, so the only cold
  start is the container itself. Send a warm-up request (or the Warm Cache pattern) after idle.

## 11.3 How to modify safely (common tasks)

**Add a new API endpoint:** create a `Map…Endpoints` extension in `CacheScope.Api/Endpoints/`, inject
the services you need (they resolve from DI), and call it from `Program.cs`. Return `Results.Ok(...)`.

**Add a method to a cache layer:** add it to the interface in the layer project
(`IMemoryCacheLayer`/`IRedisCacheLayer`) *and* the implementation. Because consumers depend on the
interface, they pick it up automatically once injected.

**Change/add a write strategy:** add a value to the `WriteStrategy` enum in `Shared`, add a `case` in
`ProductCacheService.UpdateAsync`, and (if it needs background work) follow the write-behind pattern
(`IWriteBehindQueue` + a `BackgroundService`).

**Add a new trace consumer (e.g. a metrics/Kafka sink):** implement `ITraceSink` and register it in
DI. The pipeline fans out to all registered sinks — no pipeline change needed (the Observer pattern).

**Add a DI service:** register it in the appropriate `Add…` extension with the right **lifetime**
(singleton for shared state, scoped for per-request/DbContext-bound). Remember the captive-dependency
rule ([Chapter 08](08-concurrency-and-internals.md)): a singleton must not inject a scoped service —
use `IServiceScopeFactory`.

**Change a cache TTL or the default write strategy at runtime:** use the Cache Policies panel or
`PUT /api/policy`. To change the *default*, edit `CachePolicyState`'s initial values.

**Run the tests before pushing:** `dotnet test src/CacheScope.Tests`. CI also gates on them.

## 11.4 Safe deploy & rollback

- A deploy is a **new revision** of the container app. If a bad image ships, roll back by pointing the
  app at the previous image tag: `az containerapp update -n cachescope-api -g cachescope-rg --image ghcr.io/<user>/cachescope-host:<previous-sha>`.
- `az containerapp update --image` preserves env vars + scale + secrets (so image-only rollouts keep
  your Cloudflare token, single-replica setting, etc.); a full Bicep redeploy resets to the Bicep
  definition.

## 11.5 Common gotchas checklist

- **UI shows Disconnected / zeros** → CORS origin not allowed, or backend cold-starting (the client
  retries the initial connect).
- **`X-Served-By` always Database** → caches empty (cold start) or a write strategy that invalidates
  (cache-aside) just ran; read again.
- **Stampede demo shows small "unprotected" number** → real DB is too fast; set
  `Database__SimulatedQueryLatencyMs` to widen the miss window.
- **L0 edge card empty** → Cloudflare token/zone not configured, or analytics is delayed a few
  minutes.
- **L1 Measure returns 0** → purge the Cloudflare edge (stale copies lack `Timing-Allow-Origin`), or
  DevTools "Disable cache" is on.
- **Local build "works on my machine" only** → run via `docker compose` to match the deployed image.
- **A leaked secret** (e.g. a token pasted somewhere) → rotate it immediately; never commit secrets
  (they belong in Azure Container App secrets).

---

**Next:** [Chapter 12](12-glossary.md) — every term in one place.
