# 09 · The UI, Feature by Feature

← [08 · Concurrency](08-concurrency-and-internals.md) · [Wiki home](README.md) · Next → [10 · Deployment](10-deployment.md)

---

The dashboard is an **Angular** SPA in [`web/`](../web). It's built from **standalone components**
using **signals** (reactive state that re-renders the UI when it changes). Two services do all the
talking to the backend:

- [`SignalrService`](../web/src/app/core/signalr.service.ts) — opens the WebSocket to `/hubs/traces`
  and exposes signals: `connected`, `stats`, `traces`, `trafficStatus`, `paused`.
- [`ApiService`](../web/src/app/core/api.service.ts) — REST calls (products, traffic, analytics,
  policy, cache ops, stampede, trace detail, L1 probe).

The root component [`App`](../web/src/app/app.ts) composes the panels below. Each panel is explained
as **what it shows · how to use it · the code behind it**.

## 9.1 Top stat cards

- **Shows:** Total Requests, Cache Hit Ratio (share served by any cache layer, i.e. not the DB),
  Avg Latency, Peak Latency, Failed (status ≥ 500).
- **How:** updates live as traffic flows.
- **Code:** bound to `hub.stats()` (the `LiveStatsSnapshot` pushed every 250ms over SignalR via
  `ReceiveStats`). No polling.

## 9.2 Layer chips + Cloudflare Edge (L0) card

- **Shows:** one chip per **server-measured** layer — **Browser (this browser)**, **Memory**,
  **Redis**, **Database** — with counts; and a dedicated **Cloudflare Edge (L0)** card showing the
  edge hit ratio + hit/miss/dynamic breakdown.
- **Why this split:** the top row is what the origin can *measure* (L2–L4, plus the client-measured L1
  probe). **L0 is shown separately** because it's an out-of-band aggregate from Cloudflare — it can't
  be a live per-request counter ([Chapter 04](04-the-five-cache-layers.md)). The always-0 "Cloudflare"
  chip was removed to avoid implying otherwise.
- **Code:** chips call `layerCount(layer)` (from `hub.stats()`, except Browser which reads the L1
  probe result). The edge card reads `edge()`, refreshed every 5s from `GET /api/analytics`
  (`cloudflareEdge`).

## 9.3 Cache Stampede Demo

- **Shows:** a side-by-side comparison — **Without protection** (N concurrent requests → ~N DB
  queries) vs **With single-flight** (→ 1) — as two bars, plus the "queries prevented" verdict.
- **How to use:** set a hot-key id + concurrency, click **Run demo**. It's a *real* measured run, not
  a canned number.
- **Code:** [`StampedePanel`](../web/src/app/stampede/stampede-panel.ts) → `POST /api/stampede` →
  [`StampedeRunner`](../src/CacheScope.Api/Caching/StampedeRunner.cs) invalidates the key, fires N
  concurrent requests twice (protection off/on), and returns the two DB-query counts.

## 9.4 Cache Policies

- **Shows:** the runtime cache policy — **write strategy**, memory **expiration mode**
  (absolute/sliding), memory TTL, Redis TTL — with an **Apply** button.
- **How to use / expected effect:** pick a write strategy and Apply; it takes effect **immediately
  for all requests** (not just generated traffic). Then do a write and watch the *next* read's layer:
  cache-aside → `Database` (invalidated); write-through → `Memory` (updated in place). TTLs change how
  long reads stay cached; sliding vs absolute changes when memory entries expire.
- **Code:** [`CacheControlPanel`](../web/src/app/cache-control/cache-control-panel.ts) →
  `GET/PUT /api/policy` → [`CachePolicyState`](../src/CacheScope.Shared/Caching/CachePolicyState.cs).

## 9.5 Cache Operations

- **Shows:** buttons — Clear Memory, Clear Redis, Warm Memory, Warm Redis, Purge Cloudflare, Flush
  Everything — plus a product-id field with **Expire** and **Invalidate**.
- **How to use:** manipulate cache state, then read a product and watch which layer serves it.
  - **Expire** = simulate TTL elapsing for that key (drop it so the next read reloads).
  - **Invalidate** = drop a stale copy because the data changed. (Same mechanical effect here;
    different intent.)
  - **Warm** = pre-load products into a layer so reads hit it.
  - **Purge Cloudflare** = clear the L0 edge (needs a purge-scoped token; otherwise a no-op message).
- **Code:** `CacheControlPanel` → `POST /api/cache/*` → [`CacheOperations`](../src/CacheScope.Api/Caching/CacheOperations.cs).

## 9.6 Traffic Generator (server-side load)

- **Shows:** a config form + **Start/Stop** + a **Live Traffic Panel** (current RPS, completed,
  pending, failed, avg/peak latency, elapsed, progress).
- **The config fields:**
  - **Pattern** — the 10 workloads (Cold Start, Warm Cache, Steady, Burst, Hot Key, Random, **Zipf**,
    Cache Stampede, Bot, Mixed). Each shapes which keys are hit and how the rate behaves.
  - **Key selection** — Single Hot Key / Top-N Hot Keys / Random / Sequential.
  - **Total requests**, **Requests/sec** (target rate), **Duration**, **Concurrency** (max in-flight),
    **GET %** (read/write split; the remainder are writes), **Hot keys** (size N of the hot set for
    Top-N/Zipf/Mixed).
- **Important:** this runs **server-side** (in-process) — it drives L2/L3/L4 and does **not** touch
  L0/L1. When most reads show as `Memory`, that's the cache *working*, not a bug.
- **Code:** the form in [`App`](../web/src/app/app.ts) → `POST /api/traffic/start` →
  [`TrafficRunner`](../src/CacheScope.TrafficGenerator/TrafficRunner.cs); status arrives live via
  `ReceiveTrafficStatus`.

## 9.7 Client-side burst + Measure L1

- **Shows:** a **Generate Traffic** (burst size) button, plus **Measure L1**, Pause/Resume, Clear,
  layer filter, and search.
- **The client-side burst** fires requests **from your browser** through Cloudflare — so unlike the
  server generator, these travel the real network path and *can* be served by L0/L1. It's how you
  generate real edge/browser traffic.
- **Measure L1** warms then re-requests products from *this* browser and inspects the **Resource
  Timing API** (`transferSize === 0` ⇒ served from browser cache) to count L1 hits. It's per-browser
  by nature (a browser cache is private to its device). Requires the API's `Timing-Allow-Origin`
  header (cross-origin timing) and a purged edge cache carrying that header.
- **Code:** `App.fireBurst` / `App.probeL1` → `ApiService.burst` / `ApiService.probeBrowserCache`.

## 9.8 Live Request Stream

- **Shows:** every request as it happens — id, method, path, the layer that served it (colored pill),
  status, and latency. Filterable by layer and searchable; pausable.
- **How to use:** click any row to open its **per-request timing waterfall** (per-layer segments +
  correlation id + OpenTelemetry trace id).
- **Code:** rows come from `hub.traces()` (batched `ReceiveTraces` pushes, capped at 500 rows client-
  side). Clicking calls `GET /api/traces/{correlationId}` →
  [`RequestDetailStore`](../src/CacheScope.Analytics/RequestDetailStore.cs).

## 9.9 Cache Analytics (charts)

- **Shows:** metric cards (P50/P95/P99, DB queries executed vs **prevented**, avg query time) and
  charts — request-distribution donut, hit-ratio gauge, per-layer bars, latency-over-time line,
  RPS-over-time area — plus a **Reset counters** button.
- **How to use:** run one workload, note the numbers, Reset, run another, compare.
- **Code:** [`AnalyticsPanel`](../web/src/app/analytics/analytics-panel.ts) polls `GET /api/analytics`
  every 1s. Charts are **hand-rolled SVG/CSS** (conic-gradient donut/gauge, SVG polyline/area) — no
  charting library, for a tiny dependency-free bundle.

## 9.10 How the client stays live (recap)

`SignalrService` opens the WebSocket with auto-reconnect *and* an initial-connect retry (for a
scale-to-zero backend that may be waking up). Handlers update signals; Angular's signal change
detection re-renders only what changed. The analytics panel and the edge card poll REST on timers
(1s / 5s) for data that isn't streamed.

---

**Next:** [Chapter 10](10-deployment.md) — the complete shipment process from `docker compose up` on
a laptop to the live `cachescope.dev`, every service and every step.
