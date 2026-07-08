# 06 · Code Walkthrough — Project by Project

← [05 · Request Lifecycle](05-request-lifecycle.md) · [Wiki home](README.md) · Next → [07 · Technologies](07-technologies-explained.md)

---

This chapter opens every backend project and explains each important file: **what it is, why it
exists, and how it participates in a request.** Frameworks are explained in
[Chapter 07](07-technologies-explained.md); concurrency details in [Chapter 08](08-concurrency-and-internals.md).

A recurring .NET idiom you'll see: the **primary constructor**, e.g.
`public sealed class Foo(IBar bar) : IFoo`. The parameters in parentheses are the class's
**dependencies**, supplied by Dependency Injection ([3.5](03-system-architecture.md)); you use them
directly in methods.

---

## 6.1 CacheScope.Shared — the contracts

No logic, no dependencies — just the interfaces, data records, and enums everyone else depends on.

- **`CacheLayer.cs`** — an `enum` naming the five layers (`Cloudflare, Browser, Memory, Redis,
  Database`). Used everywhere a "which layer" value is needed.
- **`CacheOutcome.cs`** — `enum { Hit, Miss }`.
- **`Models/Product.cs`** — the domain entity: `Id, Name, Category, Price, Stock, Version,
  UpdatedAt`. `Version` is bumped on each write and used to build the `ETag`.
- **`Caching/CacheReadResult.cs`** — the generic result of a pipeline read: the `Value`, the
  `ServedBy` layer, the `Outcome`, and the per-layer `Segments` (timing waterfall). Factory helpers
  `Hit(...)` and `NotFound()` keep call sites tidy.
- **`Caching/IRequestExecutor.cs`** — the abstraction the traffic generator uses to run a request
  through the pipeline *without depending on the Api project* (breaks the dependency cycle). Returns
  a `RequestExecution` (result + trace).
- **`Caching/CachePolicy.cs` + `CachePolicyState.cs`** — the runtime-mutable policy: `WriteStrategy`,
  `ExpirationMode` (absolute/sliding), memory/Redis TTLs, and the `StampedeProtection` flag.
  `CachePolicyState` is the thread-safe implementation (guarded by a lock).
- **`Caching/IWriteBehindQueue.cs`** — the queue interface + a `Channel`-backed implementation for
  the write-behind strategy (writes are enqueued here and persisted later by a background service).
- **`Tracing/`** — `RequestTrace` (the lean per-request record streamed to the dashboard),
  `RequestDetail` + `LayerTiming` (the full record with the timing waterfall + trace id),
  `ITraceSink` (the fan-out interface), `IRequestCounter` + `RequestCounter` (monotonic request ids),
  `IRequestDetailStore`, and `CorrelationConstants` (header names, the OpenTelemetry source name).
- **`Analytics/`** — `ILiveStats` + `LiveStatsSnapshot` (rolling counts/ratios/percentiles),
  `IMetricsTimeline` + `MetricsTimelinePoint` (per-second time series), `IDatabaseMetrics`,
  `EdgeStats` (L0 snapshot + cache), `AnalyticsSnapshot` (the whole analytics payload).
- **`Traffic/`** — `TrafficConfig`, `TrafficPattern` (the 10 workload patterns), `KeySelectionMode`,
  `TrafficMode`, `TrafficRunStatus`, and the sink interfaces `ITrafficStatusSink` + `ITrafficSupport`.
- **`Operations/`** — `ICacheOperations`, `ICloudflarePurger`, `StampedeResult`.

**Why so many interfaces?** Every one is a seam that lets a consumer depend on an *abstraction* it
can fake in tests and that the Host can bind to a real implementation. Remove `Shared` and the whole
dependency-inversion structure collapses into a tangle of direct references.

## 6.2 CacheScope.MemoryCache — L2

- **`IMemoryCacheLayer.cs`** — `TryGet<T>`, `Set<T>`, `Remove`, `Clear`.
- **`MemoryCacheLayer.cs`** — wraps .NET `IMemoryCache`. `Set` reads the TTL + expiration mode from
  `ICachePolicy`. The interesting part is `Clear()`: `IMemoryCache` has *no* "remove everything"
  method, so every entry is linked to a shared `CancellationTokenSource`; cancelling it evicts them
  all at once, and a fresh token source is installed for future entries (detail in
  [Chapter 08](08-concurrency-and-internals.md)).
- **`MemoryCacheOptions.cs`** — default TTL/expiration bound from configuration.
- **`MemoryCacheServiceCollectionExtensions.cs`** — the `AddMemoryCacheLayer(config)` DI registration.

## 6.3 CacheScope.RedisCache — L3

- **`IRedisCacheLayer.cs`** — `GetAsync<T>`, `SetAsync<T>`, `RemoveAsync`, `ExpireNowAsync`,
  `FlushAsync`, `IsConnected`.
- **`RedisCacheLayer.cs`** — the L3 mechanics: prefixes keys (`cachescope:`), serializes values to
  JSON (`System.Text.Json`, static reusable options), sets value+TTL atomically, tolerates a nil as a
  miss, and flushes only namespaced keys via `SCAN`. Uses the `(string)` cast on `RedisValue` to
  resolve the string-vs-bytes overload ambiguity.
- **`RedisCacheServiceCollectionExtensions.cs`** — registers a **singleton** `IConnectionMultiplexer`
  (the expensive, thread-safe Redis connection you must share) with `AbortOnConnectFail = false` for
  resilient startup, plus the layer.

## 6.4 CacheScope.Database — L4

- **`CacheScopeDbContext.cs`** — the EF Core **DbContext** (your typed gateway to the database). It
  declares `DbSet<Product> Products` and configures the model (key, max lengths, precision) and the
  seed data in `OnModelCreating`.
- **`ProductSeed.cs`** — deterministically generates 100 products (fixed timestamps so it can live
  inside an EF migration). 100 gives the traffic patterns room (hot keys, top-N, Zipf).
- **`IProductStore.cs` / `ProductStore.cs`** — the L4 accessor: `GetByIdAsync`, `GetAllIdsAsync`,
  `UpdateAsync`. Each measures its own elapsed time and records it into `IDatabaseMetrics`, plus an
  optional `SimulatedQueryLatencyMs` delay to make the demo dramatic.
- **`DatabaseMetrics.cs`** — thread-safe (`Interlocked`) counters: queries executed, **queries
  prevented**, total/average query time.
- **`DatabaseOptions.cs`** — connection string section + the simulated-latency knob.
- **`CacheScopeDbContextFactory.cs`** — a *design-time* factory so `dotnet ef` tooling can build the
  model for migrations without running the Host's startup.
- **`Migrations/`** — the generated schema (`InitialCreate`) + model snapshot. A **migration** is a
  versioned description of the database schema; `MigrateAsync()` applies it at startup.
- **`DatabaseServiceCollectionExtensions.cs`** — `AddDatabaseLayer(config)`: registers the DbContext
  (scoped), the store (scoped), and the metrics (singleton).

## 6.5 CacheScope.Api — orchestration + HTTP surface (the heart)

### Caching/
- **`ProductKeys.cs`** — the single cache-key builder (`product:{id}`).
- **`ProductCacheService.cs`** — **the cache-aside orchestrator** and the most important file in the
  project. `GetAsync` runs the L2→L3→L4 cascade (with OpenTelemetry spans, timing segments,
  stampede-protected DB load, and upward repopulation). `UpdateAsync` switches on the write strategy
  (cache-aside / write-through / write-behind / refresh-ahead). Walked in detail in [6.9](#69-deep-dive-productcacheservice).
- **`RequestExecutor.cs`** — the shared runner used by both endpoints and the traffic generator. It
  times the pipeline, builds the `RequestTrace` + `RequestDetail`, records the detail, and fans the
  trace out to every `ITraceSink`.
- **`SingleFlight.cs`** — request coalescing: a `ConcurrentDictionary<string, Lazy<Task<Product?>>>`
  so concurrent misses for one key share a single DB load (stampede protection).
- **`StampedeRunner.cs`** — the demo engine: invalidates a hot key, fires N concurrent requests
  twice (protection off, then on), and measures the DB-query delta each time.
- **`RefreshAheadScheduler.cs`** — a singleton that, on reads of near-expiry hot keys, reloads them
  in the background (using `IServiceScopeFactory` for a fresh DB scope).
- **`WriteBehindFlusher.cs`** — a `BackgroundService` that drains the write-behind queue and persists
  buffered writes to the DB off the request path.
- **`CacheOperations.cs`** — implements `ICacheOperations`: clear/warm memory & Redis, expire/
  invalidate a product, flush all, purge Cloudflare.
- **`TrafficSupport.cs`** — implements `ITrafficSupport` (the cache-admin ops the traffic generator
  needs: key universe, DB resume, clear, expire), so the generator stays decoupled.

### Endpoints/ (minimal-API route maps)
- **`ProductEndpoints.cs`** — `GET /api/products`, `GET /api/products/{id}` (runs through
  `RequestExecutor`, sets `ETag`/`Cache-Control`/`X-Served-By`, handles `304`), `PUT /api/products/{id}`.
- **`TrafficEndpoints.cs`** — `POST /api/traffic/start|stop`, `GET /api/traffic/status`.
- **`AnalyticsEndpoints.cs`** — `GET /api/analytics` (composes the `AnalyticsSnapshot` incl. the L0
  edge stats), `POST /api/analytics/reset`.
- **`CacheOpsEndpoints.cs`** — `POST /api/cache/*` (clear/warm/expire/invalidate/flush/purge) and
  `GET|PUT /api/policy`.
- **`StampedeEndpoints.cs`** — `POST /api/stampede`.
- **`ObservabilityEndpoints.cs`** — `GET /api/traces/recent`, `GET /api/traces/{correlationId}`.

### Tracing/
- **`LoggingTraceSink.cs`** — the simplest `ITraceSink`: writes each trace to the log.

### ApiServiceCollectionExtensions.cs
`AddCacheScopePipeline(config)` — the single call the Host uses to register the whole pipeline:
policy, the three layers, the orchestrator + executor + traffic-support + cache-ops (scoped), the
counter + logging sink + write-behind queue + flusher + refresh-ahead + single-flight + stampede
runner (singletons). Lifetimes are explained in [Chapter 08](08-concurrency-and-internals.md).

## 6.6 CacheScope.Analytics — measurement

- **`LiveStats.cs`** — lock-free (`Interlocked`) rolling aggregate: per-layer counts, hit ratio,
  average/peak latency, plus a `LatencyHistogram` and CF-status tally. `Snapshot()` produces the
  immutable `LiveStatsSnapshot`.
- **`LatencyHistogram.cs`** — fixed-bucket histogram for streaming P50/P95/P99 with **no per-tick
  sorting** (record = one `Interlocked` increment; percentile = walk the buckets + interpolate).
- **`MetricsTimeline.cs`** — a bounded ring of per-second `MetricsTimelinePoint`s for the charts.
- **`RequestDetailStore.cs`** — keeps recent `RequestDetail`s in a ring, indexed by correlation id,
  for the click-to-inspect waterfall.
- **`AnalyticsServiceCollectionExtensions.cs`** — registers all four as singletons.

## 6.7 CacheScope.Realtime — SignalR transport

- **`TraceHub.cs`** — the SignalR **hub** at `/hubs/traces`; defines the client method names
  (`ReceiveTraces`, `ReceiveStats`, `ReceiveTimeline`, `ReceiveTrafficStatus`) and sends the current
  stats on connect.
- **`SignalRTraceSink.cs`** — an `ITraceSink` that records stats synchronously and queues traces to a
  bounded, drop-oldest `Channel` (backpressure).
- **`SignalRTrafficStatusSink.cs`** — broadcasts traffic-run status.
- **`TraceBroadcastService.cs`** — a `BackgroundService` with three loops: batch+flush traces every
  ~100ms, broadcast stats every 250ms, sample+broadcast the timeline every 1s.
- **`RealtimeOptions.cs`** — the channel capacity, batch size, and cadences.
- **`RealtimeServiceCollectionExtensions.cs`** — `AddRealtime(config)`: SignalR + JSON protocol
  (enums as strings) + the sinks + the broadcast service.

## 6.8 CacheScope.Cloudflare — L0 integration

- **`CloudflareOptions.cs`** — zone id, API token, analytics window/poll settings.
- **`CloudflarePurger.cs`** — implements `ICloudflarePurger`: calls the Cloudflare v4 API to purge the
  edge; a safe no-op with a message when unconfigured.
- **`CloudflareEdgeStatsClient.cs`** — queries the Cloudflare **GraphQL Analytics API** for requests
  grouped by `cacheStatus` (the only way to see L0 hits) and parses them into an `EdgeStatsSnapshot`.
- **`CloudflareEdgePoller.cs`** — a `BackgroundService` that refreshes the edge-stats cache on a
  timer (only if configured).
- **`CloudflareServiceCollectionExtensions.cs`** — `AddCloudflare(config)`: the purger (typed
  HttpClient), the edge-stats cache (singleton), the stats client, and the poller.

## 6.9 CacheScope.Host — the composition root

- **`Program.cs`** — the entry point (top-level statements). Builds config + the DI container,
  registers OpenTelemetry, calls every `Add*` extension, configures CORS/health/OpenAPI, applies EF
  migrations at startup, and maps all the endpoint groups + the SignalR hub. Walked line-by-line in
  [Chapter 07](07-technologies-explained.md) (ASP.NET Core) and [Chapter 08](08-concurrency-and-internals.md) (DI).
- **`Middleware/CorrelationIdMiddleware.cs`** — request identity + response headers (see
  [Chapter 05](05-request-lifecycle.md)).
- **`HealthChecks/RedisHealthCheck.cs`, `SqlHealthCheck.cs`** — readiness probes for `/health/ready`.

## 6.10 CacheScope.Tests

xUnit tests + in-memory fakes (`FakeMemoryLayer`, `FakeRedisLayer`, `FakeProductStore`,
`StubScopeFactory`). Cover the orchestrator (cache-aside vs write-through, the waterfall, Redis→cascade),
single-flight coalescing, latency percentiles, live stats, the Zipf/key selector, and cache policy.
`dotnet test src/CacheScope.Tests` runs them; CI runs them before every deploy.

---

## 6.9 Deep dive: `ProductCacheService`

The single most important class. Its constructor lists everything the pipeline needs:

```csharp
public sealed class ProductCacheService(
    IMemoryCacheLayer memory, IRedisCacheLayer redis, IProductStore store,
    IDatabaseMetrics dbMetrics, ICachePolicy policy, IWriteBehindQueue writeBehind,
    RefreshAheadScheduler refreshAhead, SingleFlight singleFlight,
    ActivitySource activitySource, ILogger<ProductCacheService> logger) : IProductCacheService
```

`GetAsync(id)`:
1. Build key `product:{id}`; create a `segments` list for the waterfall.
2. **L2:** open span `cache.memory`; `memory.TryGet`. On hit → record a `Hit` timing,
   `dbMetrics.RecordPrevented()`, (if refresh-ahead) schedule a possible refresh, **return**.
3. **L3:** open span `cache.redis`; `await redis.GetAsync` in try/catch (Redis error ⇒ treated as
   miss). On hit → `memory.Set` (repopulate L2), `RecordPrevented()`, **return**.
4. **L4:** open span `cache.database`; load via `SingleFlight` if `policy.StampedeProtection`, else
   directly. The private `LoadAndPopulateAsync` runs the DB query and repopulates Redis + memory.
   Return `Hit(Database)` or `NotFound`.

`UpdateAsync(id, mutate)` switches on `policy.WriteStrategy` to one of `CacheAsideAsync` (write DB +
delete caches), `WriteThroughAsync` (write DB + overwrite caches), or `WriteBehindAsync` (update
caches + enqueue DB write). See [Chapter 08](08-concurrency-and-internals.md) for the async/thread
reasoning and [4.8](04-the-five-cache-layers.md) for the strategy semantics.

---

**Next:** [Chapter 07](07-technologies-explained.md) explains every framework and technology used
here — ASP.NET Core, EF Core, Redis, SignalR, OpenTelemetry, Docker, Bicep, Azure, Cloudflare — from
first principles.
