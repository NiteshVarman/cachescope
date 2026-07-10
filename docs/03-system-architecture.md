# 03 · System Architecture

← [02 · Foundations](02-foundational-concepts.md) · [Wiki home](README.md) · Next → [04 · The Five Cache Layers](04-the-five-cache-layers.md)

---

## 3.1 The big picture

CacheScope has **two deployable pieces** and several backing services:

```
 ┌────────────────────────────────────────────────────────────────────────────┐
 │                              THE INTERNET                                    │
 │                                                                              │
 │   Browser ──https──▶  Cloudflare (CDN/edge = L0)  ──https──▶  Azure          │
 │      │                    │  serves the Angular UI       │                   │
 │      │                    │  caches /api/products/* (L0)  │                   │
 │      ▼                    ▼                               ▼                   │
 │   Angular SPA        (edge PoPs worldwide)     ┌──────────────────────────┐  │
 │   (the dashboard)                              │ Azure Container Apps env  │  │
 │      ▲                                         │                           │  │
 │      │  WebSocket (SignalR) + HTTPS (REST)     │  ┌─────────────────────┐  │  │
 │      └─────────────────────────────────────────▶│ CacheScope.Host (API)│  │  │
 │                                                │  │  + IMemoryCache (L2) │  │  │
 │                                                │  └──────────┬──────────┘  │  │
 │                                                │             │              │  │
 │                                                │   ┌─────────▼────────┐     │  │
 │                                                │   │ Redis container  │(L3) │  │
 │                                                │   └──────────────────┘     │  │
 │                                                └───────────┬───────────────┘  │
 │                                                            │                   │
 │                                             ┌──────────────▼─────────────┐    │
 │                                             │ SQLite file, in-process (L4)│    │
 │                                             └────────────────────────────┘    │
 └────────────────────────────────────────────────────────────────────────────┘
```

- **Piece 1 — the backend API** (`CacheScope.Host`): an ASP.NET Core process running in an Azure
  container. It *is* L2 (holds the in-process memory cache) and L4 (an embedded SQLite file lives
  inside the same process), talks to the Redis container (L3), and pushes live updates over SignalR.
- **Piece 2 — the frontend** (`web/`): an Angular dashboard, built to static files and hosted on
  Cloudflare, served at `cachescope.dev`.
- **Backing services:** Cloudflare (DNS + edge cache L0 + UI hosting), a Redis container (L3),
  Application Insights (telemetry), GitHub Container Registry (image storage). L4 is embedded SQLite
  inside the API process — no external database service.

## 3.2 Architectural style: the "modular monolith"

> **What is it?** A **monolith** is a system that runs as *one* process (as opposed to
> **microservices**, which split a system into many separate processes that talk over the network).
> A **modular monolith** is a monolith whose code is split into many independent modules with strict
> boundaries — you get the *organization* of microservices without the *network overhead*.

**Why a monolith here (and not microservices)?** The whole point of CacheScope is to *measure the
tiny latency differences between cache layers in one request path*. If L2/L3/L4 were separate
services, every layer hop would add network latency that **dwarfs** the microsecond differences we're
trying to observe — defeating the purpose. Also, the live dashboard's counters must be **coherent**
(one source of truth), which is trivial in one process.

**Why split into modules then?** To enforce **single responsibility** and **dependency direction at
compile time**. Each concern is its own C# **project** (which compiles to its own `.dll`). Because
there is no project *reference* between, say, `RedisCache` and `Database`, the compiler makes it
*impossible* for the Redis code to accidentally reach into the database code. The boundaries are not
a convention you hope people follow — they're enforced by the build.

## 3.3 The 11 projects (what & why)

All projects live under [`src/`](../src). One is runnable (`Host`); the rest are **class libraries**
(`.dll`s loaded into the Host — see [2.1](02-foundational-concepts.md)).

| Project | Responsibility | Why it exists |
|---|---|---|
| **CacheScope.Shared** | Pure contracts + data types (interfaces, DTOs, enums). No logic, no dependencies. | The **dependency sink**: everyone depends on it; it depends on nothing. This is what lets modules talk through *abstractions* instead of each other. |
| **CacheScope.MemoryCache** | L2 — wraps .NET `IMemoryCache`. | Isolates the in-process cache behind an interface (`IMemoryCacheLayer`) so it's swappable and testable. |
| **CacheScope.RedisCache** | L3 — wraps Redis (`StackExchange.Redis`). | Isolates distributed-cache concerns: connection, serialization, key prefixing, flush/expire. |
| **CacheScope.Database** | L4 — EF Core `DbContext` over embedded SQLite, the product store, DB metrics, schema+seed created on boot. | Owns the source of truth and the "queries executed vs prevented" metric. |
| **CacheScope.Cloudflare** | L0 integration — edge purge + edge-stats poller (Cloudflare API). | Isolates the one component that talks to an external HTTP API; safe no-op when unconfigured. |
| **CacheScope.Analytics** | Rolling stats, latency percentiles, time-series, request-detail store. | Owns *measurement/aggregation* — separate from *collection* and *transport*. |
| **CacheScope.TrafficGenerator** | The load-test engine (patterns, RPS, concurrency). | Encapsulates load generation; depends only on `Shared` abstractions, never on the pipeline internals. |
| **CacheScope.Realtime** | SignalR hub + broadcasters (traces, stats, timeline). | Owns realtime transport + backpressure; the pipeline doesn't know SignalR exists. |
| **CacheScope.Api** | The orchestration + HTTP surface: the cache-aside orchestrator, endpoints, cache operations, single-flight, stampede runner. | Where the layers are *composed into behaviour*. The layers are mechanisms; this is policy. |
| **CacheScope.Host** | The runnable web app: `Program.cs`, middleware, DI wiring, health checks. | The **composition root** — the only project that knows about *everything* and wires it together. |
| **CacheScope.Tests** | xUnit unit tests + in-memory fakes. | Verifies the load-bearing logic without needing real infrastructure. |

## 3.4 The dependency graph (and the golden rule)

**The golden rule: all arrows point toward `Shared`; nothing points back.** This is **Dependency
Inversion** — high-level and low-level modules both depend on *abstractions* (in `Shared`), not on
each other.

```
                         ┌─────────────────────┐
                         │  CacheScope.Shared  │  (interfaces + DTOs; depends on nothing)
                         └──────────▲──────────┘
        ┌───────────┬───────────┬───┴───┬───────────┬───────────┬──────────┐
        │           │           │       │           │           │          │
  MemoryCache   RedisCache   Database  Cloudflare  Analytics  Realtime  TrafficGenerator
        ▲           ▲           ▲                                            ▲
        └─────┬─────┴─────┬─────┘                                            │
              │           │                                                  │
           ┌──┴───────────┴──────────────────────────────────────────────┐  │
           │                     CacheScope.Api  ─────────────────────────┼──┘ (Api → TrafficGenerator)
           └───────────────────────────────▲──────────────────────────────┘
                                            │
                                    ┌───────┴────────┐
                                    │ CacheScope.Host │  (references EVERYTHING)
                                    └────────────────┘
```

Two subtleties worth understanding deeply (common interview questions):

1. **`Api` does *not* reference `Analytics` or `Realtime`.** The analytics endpoints work purely
   against `Shared` interfaces (`ILiveStats`, `IDatabaseMetrics`, `IMetricsTimeline`), and the trace
   sinks are `Shared` interfaces too. Only the **Host** binds the concrete implementations. So `Api`
   is decoupled from *both* the analytics implementation and the SignalR implementation.
2. **`Api` references `TrafficGenerator`, but `TrafficGenerator` references only `Shared`.** No
   cycle. The traffic engine calls *back into* the pipeline via the `IRequestExecutor` interface
   (defined in `Shared`) — classic Dependency Inversion used to break what would otherwise be a
   circular dependency.

## 3.5 The "composition root" and Dependency Injection

> **Dependency Injection (DI):** instead of a class creating its own collaborators
> (`new RedisCacheLayer()`), it *declares* what it needs as constructor parameters, and a central
> **container** supplies them at runtime. This lets classes depend on **interfaces**, which makes
> them swappable and testable.

There is exactly one place that knows how to build everything: [`Program.cs`](../src/CacheScope.Host/Program.cs)
in the Host. It registers every interface→implementation mapping into the DI **container** at
startup. When a request needs, say, an `IProductCacheService`, the container constructs it and
recursively supplies all *its* dependencies. This is covered line-by-line in
[Chapter 06](06-code-walkthrough.md) and the lifetime rules in [Chapter 08](08-concurrency-and-internals.md).

## 3.6 Two cross-cutting patterns you'll see everywhere

**(a) The "sink" fan-out (Observer pattern).** The request pipeline doesn't call SignalR or the
logger directly. It publishes a `RequestTrace` to every registered `ITraceSink`. Today there are two
sinks (a logging sink and a SignalR sink); you could add a third (e.g. Kafka) without touching the
pipeline. The pipeline depends only on the `ITraceSink` abstraction.

```
   RequestExecutor ──publishes trace──▶ [ ITraceSink #1: Logging ]
                                        [ ITraceSink #2: SignalR (→ dashboard) ]
                                        [ ...add more without changing the pipeline ]
```

**(b) Background work from singletons via `IServiceScopeFactory`.** Some components (the stampede
runner, refresh-ahead scheduler, write-behind flusher) are long-lived **singletons** but need to do
database work, and the database context is **scoped** (per-request). A singleton can't safely hold a
scoped object. So they inject a **scope factory** and create a fresh scope for each background
operation. (Full explanation in [Chapter 08](08-concurrency-and-internals.md).)

## 3.7 Why it runs as a single instance (important)

CacheScope is deliberately pinned to **one running container instance** (`maxReplicas: 1`). Here's
why: its live state — the stats counters, the traffic-run status, the SignalR connections — lives
**in the process's memory** (in-process singletons). If Azure ran two copies, each would have its own
independent counters, and requests would be spread across them, so the dashboard's numbers would be
*fragmented and inconsistent* (we actually hit this bug in production — see
[Chapter 11](11-operations-runbook.md)). For an *observability* tool, coherent numbers matter more
than horizontal scale. A genuinely scaled version would need shared state (e.g. Azure SignalR Service
+ a Redis-backed store) — a real trade-off worth being able to articulate.

## 3.8 Folder map

```
Multi Layer Caching/
├── src/                      # the .NET backend (11 projects)
│   ├── CacheScope.Host/       # runnable web app (entry point)
│   ├── CacheScope.Api/        # orchestration + endpoints
│   ├── CacheScope.Shared/     # contracts (the dependency sink)
│   ├── CacheScope.MemoryCache/ CacheScope.RedisCache/ CacheScope.Database/
│   ├── CacheScope.Cloudflare/ CacheScope.Analytics/ CacheScope.TrafficGenerator/
│   ├── CacheScope.Realtime/   CacheScope.Tests/
│   ├── Dockerfile             # builds the Host image
│   └── Directory.Build.props  # solution-wide build settings
├── web/                      # the Angular dashboard (frontend)
├── infra/                    # Bicep IaC + deploy/teardown scripts
├── wrangler.jsonc            # Cloudflare frontend deploy config
├── .github/workflows/        # CI/CD (GitHub Actions)
├── docker-compose.yml        # local dev (Redis + SQL + Host)
└── docs/                     # THIS WIKI
```

---

**Next:** [Chapter 04](04-the-five-cache-layers.md) goes deep on each of the five cache layers — where
each physically stores data, how it's measured, and the exact code involved.
