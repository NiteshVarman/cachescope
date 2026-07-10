# CacheScope

**Multi-Layer Cache Performance & Observability Platform**

🔗 **Live:** [cachescope.dev](https://cachescope.dev) &nbsp;·&nbsp; **API:** [api.cachescope.dev](https://api.cachescope.dev/health)

> 📖 **New to the project? Read the [Engineering Wiki](docs/README.md)** — a from-first-principles
> handbook (architecture, every file, the request lifecycle, deployment, and an operations runbook)
> that lets a brand-new engineer understand the entire system end to end.

CacheScope is a production-inspired platform-engineering tool that **visualizes, analyzes, and simulates** the full lifecycle of a request as it travels through five caching layers. It is deliberately **not** a CRUD app — its purpose is to make cache behavior *observable*: where every request is served, how caching reduces latency and database load, and how the system behaves under configurable synthetic traffic.

Every request emits a trace (which layer served it, latency, hit/miss, correlation id) that streams **live** to an Angular dashboard over SignalR, alongside real-time analytics, a load-testing traffic generator, runtime cache controls, and a cache-stampede demonstration.

---

## Cache layers

| Layer | Component | Where it lives | How it's observed |
|-------|-----------|----------------|-------------------|
| **L0** | Cloudflare edge cache | Cloudflare POPs | Cloudflare **GraphQL Analytics API** (edge hit ratio) |
| **L1** | Browser cache | Client | client-side **Resource Timing** probe |
| **L2** | Application memory cache | In-process (`IMemoryCache`) | Directly measured in the pipeline |
| **L3** | Distributed cache | Redis | Directly measured in the pipeline |
| **L4** | Database (source of truth) | Embedded SQLite (in the API container) | Directly measured in the pipeline |

A request checks each layer in order and is served by the **first hit**; misses cascade down to the database and repopulate the layers on the way back up (cache-aside).

## Architecture

```mermaid
flowchart LR
    subgraph Client
      B[Browser cache · L1]
    end
    subgraph Edge
      CF[Cloudflare · L0]
    end
    subgraph "Azure Container Apps"
      API[ASP.NET Core Host<br/>+ IMemoryCache · L2]
      RED[(Redis container · L3)]
      HUB[SignalR TraceHub]
    end
    SQL[(Embedded SQLite · L4)]
    NG[Angular + Material dashboard]

    B --> CF --> API
    API -->|miss| RED -->|miss| SQL
    API -. traces / stats / timeline .-> HUB -. WebSocket .-> NG
    NG -->|traffic gen · ops · policy · stampede| API
    API -. OpenTelemetry .-> AI[Application Insights]
```

## Features

**Request pipeline & tracing**
- Layered L2→L3→L4 read path with cache-aside repopulation.
- `ETag` / `Cache-Control` / `304 Not Modified` so L0 and L1 participate.
- Every request emits a `RequestTrace` with a correlation id, served-by layer, hit/miss and latency.

**Live dashboard (Angular + Material)**
- Real-time stat cards, per-layer breakdown, and a filterable/searchable/pausable live request stream over SignalR.
- Click any request to open a **per-layer timing waterfall** with its correlation id and OpenTelemetry trace id.

**Traffic generator (load-test engine)**
- Self-paced dispatch to a target RPS with bounded concurrency.
- 10 workload patterns: Cold Start, Warm Cache, Steady, Burst, Hot Key, Random, **Zipf**, Cache Stampede, Bot, Mixed.
- Configurable key-selection strategies and GET/write split, with a Live Traffic Panel (RPS, completed, pending, failed, avg/peak latency).

**Analytics & charts**
- Streaming latency percentiles (P50 / P95 / P99 via a fixed-bucket histogram — no per-tick sorting).
- Per-second metrics timeline, request-distribution donut, hit-ratio gauge, per-layer bars, latency line, RPS area.
- Database analytics: queries executed vs. **queries prevented** by caching, average query time.
- **L0 edge stats** pulled from the Cloudflare GraphQL Analytics API (edge hit ratio + hit/miss/dynamic), and **L1 browser-cache** measured client-side via the Resource Timing API.
- Reset counters to compare two workloads back-to-back.

**Cache operations & policies (runtime-configurable)**
- Clear / warm memory & Redis, expire or invalidate a product, flush everything, purge the Cloudflare edge.
- Live-switchable memory/Redis TTL, absolute vs. sliding expiration, and write strategy.
- Four write strategies: **Cache-Aside**, **Write-Through**, **Write-Behind** (background flusher), **Refresh-Ahead** (proactive reload near expiry).

**Cache stampede demo**
- Expire a hot key, fire N concurrent requests, and compare **no protection** (every miss hits the DB) against **single-flight** (concurrent misses coalesce into one DB load) — side by side.

**Observability**
- OpenTelemetry spans per cache layer (`cache.memory` / `cache.redis` / `cache.database`) exported to Application Insights, so the distributed trace mirrors the pipeline.

## What's real vs. observed vs. simulated

Being precise about this, because it matters for interpreting the demo:

- **Measured directly (real):** L2 `IMemoryCache`, L3 Redis, and L4 SQL — every hit/miss and latency is recorded server-side in the pipeline.
- **Observed out-of-band (real, but not from the origin):** L0/L1 hits never reach the origin, so they can't be counted in the pipeline. **L0** is pulled from **Cloudflare's GraphQL Analytics API** (a background poller; aggregate, ~minutes delayed) and shown as the edge hit ratio. **L1** is measured **client-side** via the browser's **Resource Timing API** (`transferSize === 0` ⇒ served from cache) — a per-browser probe, since only the browser can see its own cache. The **server-side generator** drives L2–L4; the **client-side burst** generates the real browser→edge traffic that lights up L0/L1.
- **Optionally simulated:** database latency can be padded via `Database:SimulatedQueryLatencyMs`. A warm SQL answers in well under a millisecond, which hides the point of caching; a small pad makes the L2/L3-vs-L4 gap (and the stampede demo) clearly visible.

## Tech stack

| Area | Technology |
|------|------------|
| Backend | ASP.NET Core (`net10.0`) |
| Frontend | Angular + Angular Material |
| Realtime | SignalR (self-hosted) |
| Memory cache (L2) | `IMemoryCache` |
| Distributed cache (L3) | Redis |
| Database (L4) | Embedded SQLite (in-process, ephemeral, re-seeded on boot) |
| Edge / CDN (L0) | Cloudflare |
| Hosting | Azure Container Apps |
| Frontend hosting | Cloudflare |
| Container registry | GitHub Container Registry (GHCR) |
| Telemetry | OpenTelemetry → Application Insights |
| IaC | Bicep |
| CI/CD | GitHub Actions |

## Repository layout

```
.
├── src/
│   ├── CacheScope.slnx
│   ├── Directory.Build.props          # solution-wide TFM + audit config
│   ├── Dockerfile                     # multi-stage build for the Host
│   ├── CacheScope.Host/               # web host + composition root (Program.cs, middleware)
│   ├── CacheScope.Api/                # endpoints, orchestrator, request executor
│   ├── CacheScope.Shared/             # contracts: RequestTrace, CacheLayer, policy, traffic
│   ├── CacheScope.MemoryCache/        # L2
│   ├── CacheScope.RedisCache/         # L3
│   ├── CacheScope.Cloudflare/         # L0 integration (edge purge, header reading)
│   ├── CacheScope.Database/           # L4 (EF Core + SQLite, schema+seed created on boot)
│   ├── CacheScope.Analytics/          # rolling stats, percentiles, timeline, detail store
│   ├── CacheScope.TrafficGenerator/   # load-generation engine
│   ├── CacheScope.Realtime/           # SignalR hub + broadcasters
│   └── CacheScope.Tests/              # xUnit unit tests
├── web/                               # Angular + Material dashboard
├── infra/
│   ├── main.bicep                     # full Azure topology
│   ├── deploy.sh                      # one-command provision
│   └── teardown.sh                    # remove all resources
├── wrangler.jsonc                     # Cloudflare static-assets deploy for the frontend
├── .node-version                      # pins Node for the frontend build
├── .github/workflows/deploy.yml       # test → build → GHCR → Container Apps
└── docker-compose.yml                 # local dev: Redis + SQL + Host
```

## API reference

| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/health`, `/health/ready` | Liveness / readiness (Redis + SQL) |
| `GET` | `/api/products/{id}` | Read through the cache pipeline |
| `PUT` | `/api/products/{id}` | Update (behavior depends on write strategy) |
| `POST` | `/api/traffic/start` · `/stop`, `GET /api/traffic/status` | Traffic generator |
| `GET` | `/api/analytics/`, `POST /api/analytics/reset` | Analytics snapshot / reset |
| `POST` | `/api/cache/{clear-memory,clear-redis,warm-memory,warm-redis,flush,purge-cloudflare}` | Cache operations |
| `POST` | `/api/cache/{expire,invalidate}/{id}` | Per-product operations |
| `GET` · `PUT` | `/api/policy/` | Read / update cache policy |
| `POST` | `/api/stampede?hotKeyId={id}&concurrency={n}` | Run the stampede comparison |
| `GET` | `/api/traces/recent`, `/api/traces/{correlationId}` | Per-request forensics |
| — | `/hubs/traces` | SignalR hub (traces, stats, timeline, traffic status) |

## Running locally

```bash
# Backend + dependencies (Redis + SQL + Host) in containers
docker compose up --build          # API on http://localhost:5199

# Frontend
cd web
npm install                        # first time only
npm start                          # dashboard on http://localhost:4200
```

Quick pipeline check:

```bash
curl -i http://localhost:5199/health
for i in 1 2 3; do curl -s -D - -o /dev/null http://localhost:5199/api/products/42 | grep -i x-served-by; done
# => Database, then Memory, then Memory  (populates up the layers)
```

> Tip: set `Database__SimulatedQueryLatencyMs=45` when running the Host to make the
> cache-vs-database latency gap and the stampede demo visibly dramatic.

## Tests

```bash
dotnet test src/CacheScope.Tests
```

Unit tests cover the load-bearing logic — cache-aside / write-through orchestration, the single-flight coalescer, streaming percentiles, rolling stats, the Zipf/key selector, and the cache policy. CI runs them on every push before building the image.

## Deployment

The platform runs on Azure (API) with Cloudflare in front (edge + frontend hosting).

**1 — Azure infrastructure** (Container Apps environment, self-hosted Redis, Application Insights; L4 is embedded SQLite in the API container — no managed database):

```bash
RG=cachescope-rg LOCATION=centralindia \
IMAGE='ghcr.io/<user>/cachescope-host:latest' ./infra/deploy.sh
# public GHCR image needs no registry credentials; for a private image add GHCR_USER + GHCR_TOKEN
```

`./infra/teardown.sh` removes all resources.

**2 — Custom domain + Cloudflare edge:**
1. Add the domain to Cloudflare and point the registrar's nameservers at Cloudflare.
2. Add `api` as a `CNAME` to the Container App FQDN (DNS-only), and `asuid.api` as a `TXT` with the app's domain-verification id.
3. Bind the hostname and issue the managed certificate:
   ```bash
   az containerapp hostname add  -n cachescope-api -g cachescope-rg --hostname api.<domain>
   az containerapp hostname bind -n cachescope-api -g cachescope-rg --hostname api.<domain> \
     --environment cachescope-env --validation-method CNAME
   ```
4. Set the Cloudflare SSL/TLS mode to **Full (strict)**, switch the `api` record to **Proxied**, and add a cache rule for `GET /api/products/*` (respect origin TTL). Repeat product reads then return `CF-Cache-Status: HIT`.

**3 — Frontend (Cloudflare):** connect the repo, build with `cd web && npm ci && npx ng build` (output `web/dist/web/browser`); `wrangler.jsonc` + `.node-version` configure the static-assets deploy. Attach the apex/`www` custom domains.

**4 — Continuous delivery:** pushing to `main` runs the tests, builds and pushes the image to GHCR, and rolls it out to Azure Container Apps.

## Build phases

Built incrementally; every phase is complete and verified end-to-end in production.

| Phase | Delivered |
|-------|-----------|
| **0 — Foundations** | Solution structure, shared trace models, correlation-id middleware, OpenTelemetry, health/diagnostics endpoints, Docker/Compose, Bicep, CI/CD |
| **1 — Cache pipeline** | L2→L3→L4 cache-aside, ETag/`Cache-Control`/304, `X-Served-By`, request tracing, DB-queries-prevented metrics |
| **2 — Realtime + UI** | SignalR trace hub (batched, backpressured) + Angular/Material dashboard with the live request stream |
| **3 — Traffic generator** | Self-paced load engine, 10 workload patterns, key strategies, Live Traffic Panel |
| **4 — Analytics & charts** | Streaming P50/P95/P99, metrics timeline, distribution/gauge/bars/line/area, DB analytics |
| **5 — Operations & policies** | Runtime TTL/expiration/write-strategy switching; clear/warm/expire/invalidate/flush/purge |
| **6 — Stampede demo** | Single-flight vs. unprotected comparison (1000 concurrent → 1000 DB queries vs. 1) |
| **7 — Observability** | Per-request timing waterfall + per-layer OpenTelemetry spans |
| **8 — Hardening** | Unit test suite in CI, deploy/teardown scripts, architecture docs |

## License

MIT
