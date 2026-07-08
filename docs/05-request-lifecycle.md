# 05 · The Request Lifecycle

← [04 · Cache Layers](04-the-five-cache-layers.md) · [Wiki home](README.md) · Next → [06 · Code Walkthrough](06-code-walkthrough.md)

---

This chapter traces **one request** — `GET /api/products/42` — from the browser to the response, and
then follows the **live update** that flows to the dashboard. Every box is real code.

## 5.1 The full read path (diagram)

```
 BROWSER
   │  GET https://api.cachescope.dev/api/products/42
   ▼
 ── L0: Cloudflare edge ─────────────────────────────────────────────
   │  HIT? → return cached response (CF-Cache-Status: HIT). DONE. (origin never sees it)
   │  MISS/expired? → forward to origin ↓
   ▼
 ── L1: (browser already checked its own cache before L0) ───────────
   ▼
 AZURE CONTAINER APP  →  Kestrel (the web server inside CacheScope.Host)
   │
   ▼  [middleware pipeline, in order]
   1. CORS                     — is this Origin allowed to call the API?
   2. CorrelationIdMiddleware  — assign/propagate a correlation id; add response headers
   3. Routing                  — match the URL to an endpoint handler
   ▼
 ProductEndpoints.GetProduct   (the handler)
   │  resolves IRequestExecutor from DI
   ▼
 RequestExecutor.GetProductAsync   (starts a Stopwatch)
   ▼
 ProductCacheService.GetAsync  ── THE PIPELINE ──
   │   span "cache.memory":  memory.TryGet  ── HIT ─▶ return (ServedBy = Memory)
   │   span "cache.redis":   redis.GetAsync ── HIT ─▶ repopulate L2; return (ServedBy = Redis)
   │   span "cache.database":store.GetById  ─────────▶ repopulate L3+L2; return (ServedBy = Database)
   │        (if stampede protection on, the DB load goes through SingleFlight)
   ▼  returns CacheReadResult<Product> (value + ServedBy + per-layer timings)
 RequestExecutor
   │   builds a RequestTrace (lean) + RequestDetail (full, with trace id + waterfall)
   │   records the detail in IRequestDetailStore
   │   FANS OUT the trace to every ITraceSink:
   │      ├─ LoggingTraceSink  → structured log line
   │      └─ SignalRTraceSink  → stats.Record(trace)  +  channel.TryWrite(trace)   (non-blocking)
   ▼
 ProductEndpoints.GetProduct  (finishes the HTTP response)
   │   sets X-Served-By, Cache-Control: public max-age=30, ETag
   │   if If-None-Match matches → 304 Not Modified (no body)
   │   else → 200 OK + product JSON
   ▼
 CorrelationIdMiddleware  (on the way out) → stamps X-Correlation-Id + Timing-Allow-Origin
   ▼
 Kestrel writes the response → Cloudflare caches it (seeds L0) → browser caches it (seeds L1)
   ▼
 BROWSER receives the response
```

## 5.2 Step-by-step (with the "why" for each)

1. **Cloudflare (L0) / browser (L1)** may answer before the origin is ever contacted. Only a full
   edge+browser miss reaches Azure. *Why:* the fastest request is the one your server never handles.
2. **Kestrel** is the lightweight web server built into ASP.NET Core; it accepts the TCP/TLS
   connection and hands the request into the middleware pipeline.
3. **CORS middleware** — *Cross-Origin Resource Sharing*. Because the dashboard (`cachescope.dev`)
   and the API (`api.cachescope.dev`) are different origins, browsers block cross-origin calls unless
   the API explicitly allows them. This middleware adds the `Access-Control-Allow-*` headers for
   permitted origins. *Why needed:* without it the browser silently blocks every API call and the
   dashboard shows nothing.
4. **`CorrelationIdMiddleware`** ([file](../src/CacheScope.Host/Middleware/CorrelationIdMiddleware.cs))
   — gives the request an identity: it reuses an inbound `X-Correlation-Id` or Cloudflare's `CF-Ray`,
   or mints a new id; stores it in `HttpContext.Items`, tags the OpenTelemetry `Activity`, opens a
   logging scope, and registers response headers (`X-Correlation-Id`, `Timing-Allow-Origin`). *Why:*
   one id ties a request's logs, its trace, and its dashboard row together for debugging.
5. **Routing** matches `/api/products/42` to the `GetProduct` minimal-API handler.
6. **`RequestExecutor`** ([file](../src/CacheScope.Api/Caching/RequestExecutor.cs)) is the shared
   entry the endpoint *and* the traffic generator both use, so all traffic is traced identically. It
   times the pipeline and, after it returns, builds and publishes the trace.
7. **`ProductCacheService.GetAsync`** ([file](../src/CacheScope.Api/Caching/ProductCacheService.cs))
   runs the L2→L3→L4 cascade from [Chapter 04](04-the-five-cache-layers.md), wrapping each layer in an
   OpenTelemetry span and recording a `LayerTiming`.
8. **Trace fan-out** — the trace goes to every `ITraceSink`. The SignalR sink records stats
   *synchronously* (always accurate) and queues the trace to a **channel** for the live stream
   *without blocking* the request thread.
9. **HTTP shaping** — the endpoint sets `X-Served-By` (which layer answered), `Cache-Control` +
   `ETag` (so L0/L1 can cache), and returns `200`+JSON or `304`.
10. **Response out** — the `Cache-Control` header means Cloudflare stores the response at the edge
    and the browser stores it locally *on the way out* — i.e. the response you generate **seeds the
    two layers in front of you** for next time.

## 5.3 The realtime path (how the dashboard updates)

The dashboard doesn't poll — the server **pushes**. This happens on separate threads, *off* the
request's critical path:

```
 (request threads)                    (one background thread)              (browser)
 SignalRTraceSink.PublishAsync         TraceBroadcastService
   stats.Record(trace)   ──accurate                                    Angular SignalrService
   channel.TryWrite(trace)──▶ [ bounded Channel, drop-oldest ] ──drain──▶
                                        every ~100ms: batch up to 250   ──WebSocket──▶ on("ReceiveTraces")
                                        SendAsync("ReceiveTraces", batch)              → traces signal
                                        every 250ms: SendAsync("ReceiveStats")         → stats signal
                                        every 1s:    SendAsync("ReceiveTimeline")      → (analytics)
```

- The request thread only does a **non-blocking** `channel.TryWrite` and returns — it never waits for
  the dashboard. *Why:* a slow or numerous set of dashboard clients must never slow down real
  requests.
- The channel is **bounded with drop-oldest**: under extreme load it drops the oldest *stream* entries
  rather than growing memory or blocking. *Why:* the live stream is best-effort; the stats (recorded
  synchronously) stay exact.
- The broadcaster **batches** (~100ms, up to 250 traces per message) so it sends ~10 messages/sec/
  client regardless of RPS. *Why:* sending one message per request at high RPS would drown the server
  in serialization/fan-out cost.
- **SignalR** serializes each message to JSON (enums as strings) and pushes it over the **WebSocket**
  to every connected browser, where the Angular `SignalrService` updates **signals** (reactive state)
  and the UI re-renders.

Full details of this path are in [Chapter 08](08-concurrency-and-internals.md).

## 5.4 The write path (short)

`PUT /api/products/42` goes through `ProductCacheService.UpdateAsync`, which switches on the runtime
**write strategy** ([4.8](04-the-five-cache-layers.md)): cache-aside deletes the cached copies,
write-through overwrites them, write-behind updates caches now and queues the DB write, refresh-ahead
does write-through plus proactive reloads. You observe the effect by watching the *next read's*
`X-Served-By`.

## 5.5 The threading model (interview-critical)

| Stage | Thread | Blocks the request? |
|---|---|---|
| Pipeline (memory/redis/db) | the request thread (async; frees the thread during I/O waits) | only for the actual I/O it must await |
| Publish to SignalR sink | the request thread | **No** — `TryWrite` is non-blocking |
| Drain + broadcast | one background thread (`TraceBroadcastService`) | No (off the request path) |
| DB write (write-behind) | one background thread (`WriteBehindFlusher`) | No |

The guiding principle: **nothing on a request's critical path ever waits on the dashboard or on
deferred work.**

---

**Next:** [Chapter 06](06-code-walkthrough.md) opens the code project-by-project and file-by-file.
