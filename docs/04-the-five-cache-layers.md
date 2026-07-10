# 04 · The Five Cache Layers

← [03 · Architecture](03-system-architecture.md) · [Wiki home](README.md) · Next → [05 · Request Lifecycle](05-request-lifecycle.md)

---

The core idea (repeated because it's everything): **a read checks each layer from fastest to slowest
and is answered by the first that has the data; a miss cascades down to the database, which then
repopulates the faster layers on the way back up.** This is the **cache-aside** pattern.

```
        GET Product 42
              │
              ▼
   L0 Cloudflare edge? ── HIT ─▶ served worldwide, never reaches your server
              │ miss
   L1 Browser cache?   ── HIT ─▶ served on the user's device, never leaves the browser
              │ miss (reaches the origin server)
   L2 Memory?          ── HIT ─▶ return (microseconds); "DB query prevented"
              │ miss
   L3 Redis?           ── HIT ─▶ copy up into L2; return (few ms); "DB query prevented"
              │ miss
   L4 Database         ─────────▶ the source of truth; copy up into L3 + L2; return
```

## 4.1 The cache key

Every layer keys data by a string. There is **one** canonical key builder,
[`ProductKeys.For(id)`](../src/CacheScope.Api/Caching/ProductKeys.cs), which returns `"product:42"`.
Having a single source of the key format means memory and Redis never disagree about where a product
lives (a subtle class of "silent cache miss" bugs avoided).

## 4.2 L0 — Cloudflare edge cache

- **What/where:** Cloudflare is a CDN (Chapter 02) with servers in cities worldwide. When a response
  is cacheable, Cloudflare stores it at the **edge** (a server near the user). The key is the request
  URL (`/api/products/42`).
- **How it's enabled:** the API sends `Cache-Control: public, max-age=30`, and a Cloudflare **Cache
  Rule** says "cache `GET /api/products/*`." On a repeat request, Cloudflare answers from the edge
  with header `CF-Cache-Status: HIT` — **and your origin server never sees the request.**
- **Why it's "observed, not measured":** because an L0 hit never reaches your server, your server
  *cannot count it*. So L0 numbers are pulled **out-of-band** from Cloudflare's **GraphQL Analytics
  API** by a background poller ([`CloudflareEdgeStatsClient`](../src/CacheScope.Cloudflare/CloudflareEdgeStatsClient.cs)),
  aggregated and slightly delayed. This is a genuine, correct architectural truth, not a shortcut.
- **What if removed:** every request would hit your origin; you'd lose the biggest, cheapest,
  closest-to-user cache and global low latency.

## 4.3 L1 — Browser cache

- **What/where:** the browser's own HTTP cache, on the user's device. Governed entirely by the
  `Cache-Control` / `ETag` headers the server sent.
- **How:** within `max-age`, the browser serves a repeat request **from its own disk/RAM without any
  network call at all**. With `ETag`, it can cheaply revalidate (`304 Not Modified`).
- **Why "measured client-side only":** like L0, an L1 hit never leaves the browser, so no server can
  see it. CacheScope measures it **in the browser** using the **Resource Timing API**
  (`transferSize === 0` ⇒ served from cache) via the "Measure L1" probe — a per-browser measurement,
  because a browser cache is private to each device.
- **What if removed:** repeat views by the same user would needlessly re-fetch over the network.

## 4.4 L2 — Application memory cache (`IMemoryCache`)

- **What/where:** an in-process cache living in the **Host process's RAM**. Implemented by
  [`MemoryCacheLayer`](../src/CacheScope.MemoryCache/MemoryCacheLayer.cs) wrapping .NET's
  `IMemoryCache`.
- **The key property:** it stores the **actual `Product` C# object** — a reference, *no
  serialization* — because it's in the same process. That's why L2 is sub-microsecond: a hit is just
  handing back a pointer.
- **TTL & expiration:** the entry's lifetime comes from the runtime policy (`ICachePolicy`, default
  30s). Expiration can be **absolute** (dies at a fixed time after write) or **sliding** (timer resets
  on each read → hot keys never expire). See [Chapter 08](08-concurrency-and-internals.md) for the
  clever `CancellationChangeToken` trick used to implement `Clear()`.
- **Volatile & private:** gone on restart, and each process instance has its own (why we run a single
  instance — [3.7](03-system-architecture.md)).
- **What if removed:** every read would pay a network hop to Redis even for the hottest keys — the
  single biggest latency regression.

## 4.5 L3 — Redis (distributed cache)

- **What/where:** **Redis** is an in-memory key-value store running as a **separate process** (a
  container). CacheScope talks to it via the `StackExchange.Redis` library in
  [`RedisCacheLayer`](../src/CacheScope.RedisCache/RedisCacheLayer.cs).
- **Why serialization is mandatory:** Redis is a different process (Chapter 02), so a C# object can't
  be "sent" to it — a `Product` is a graph of references only meaningful in the Host's RAM. It must be
  **serialized** to a JSON string with `System.Text.Json`, sent over TCP, and **deserialized** back
  into a fresh object on read. *This serialization is exactly why L3 (a few ms) is slower than L2 (an
  in-heap reference).*
- **Keys are namespaced:** `RedisCacheLayer` prefixes keys with `cachescope:`, so the full Redis key
  is `cachescope:product:42`. This prevents collisions on a shared Redis and lets `FlushAsync` delete
  only *our* keys (`cachescope:*`) via `SCAN`, never the dangerous `KEYS`/`FLUSHALL`.
- **TTL is set atomically:** `StringSetAsync(key, json, ttl)` issues one Redis `SET ... EX` command
  — value and expiry together — avoiding a race where a crash between `SET` and `EXPIRE` leaves an
  immortal key.
- **Shared & durable:** all instances share it; it survives Host restarts (so after a Host restart,
  L2 is cold but L3 is often warm — a soft landing).
- **Resilience:** the read is wrapped in try/catch; if Redis is unreachable, it's treated as a *miss*
  and the request degrades to the database rather than failing. A cache is an accelerator, not a hard
  dependency.
- **No persistence here:** the container runs Redis with saving disabled (`--save "" --appendonly
  no`) — pure cache; data is lost on Redis restart (fine — it just re-warms from SQL).
- **What if removed:** cross-instance sharing and restart-durability disappear; more load reaches L4.

## 4.6 L4 — Database (embedded SQLite, the source of truth)

- **What/where:** a **relational database** — **SQLite**, a single file that lives *inside* the API
  process (both locally and in the cloud). The `Products` table is the authoritative store. Because
  the file is created and re-seeded on boot, it's ephemeral (a fresh, cost-free DB every start).
- **Accessed via EF Core:** [`ProductStore`](../src/CacheScope.Database/ProductStore.cs) runs
  `SELECT ... WHERE Id = @id` through Entity Framework Core, records the real query time into
  [`DatabaseMetrics`](../src/CacheScope.Database/DatabaseMetrics.cs), and can add an optional
  **simulated latency** so the cache-vs-DB gap is visible in demos.
- **The ROI metric:** `DatabaseMetrics` tracks **queries executed** vs. **queries prevented** (a
  prevented query = a cache hit at L2/L3). This number *is* the business case for caching.
- **Why SQLite (in-process):** it needs no separate database server, no connection credentials, and
  no cloud bill — the schema and 100 seed products are created at startup (see
  [Chapter 11](11-operations-runbook.md)). The trade-off is that data is not durable across restarts,
  which is exactly what we want for a demo whose DB is just "something slow to cache in front of."
- **What if removed:** there is no source of truth — nothing to cache. This is the one layer you
  cannot remove.

## 4.7 Read path, in code (summary)

The whole read cascade lives in one method,
[`ProductCacheService.GetAsync`](../src/CacheScope.Api/Caching/ProductCacheService.cs) (walked
line-by-line in [Chapter 06](06-code-walkthrough.md)). In shape:

```csharp
// L2
if (memory.TryGet(key, out product))         { RecordPrevented(); return Hit(Memory); }
// L3
product = await redis.GetAsync(key);
if (product != null) { memory.Set(key, product); RecordPrevented(); return Hit(Redis); }
// L4
product = await store.GetByIdAsync(id);       // the only real DB query
await redis.SetAsync(key, product);           // repopulate L3
memory.Set(key, product);                     // repopulate L2
return Hit(Database);
```

Each layer read is wrapped in an **OpenTelemetry span** (`cache.memory` / `cache.redis` /
`cache.database`) so the distributed trace mirrors the pipeline, and each records a `LayerTiming`
segment used for the per-request waterfall.

## 4.8 The write path & staleness (why write strategy matters)

A **write** (`PUT /api/products/42`) must not leave stale copies in the caches. The behaviour is a
**runtime-selectable write strategy** ([`ICachePolicy`](../src/CacheScope.Shared/Caching/CachePolicy.cs)):

| Strategy | On a write... | Next read is served by... |
|---|---|---|
| **Cache-Aside** (default) | write DB, **delete** cached copies | Database (then repopulates) |
| **Write-Through** | write DB, **overwrite** cached copies | Memory (already fresh) |
| **Write-Behind** | update caches now, **queue** the DB write (background flusher persists later) | Memory (eventually consistent) |
| **Refresh-Ahead** | write-through, **plus** proactively reload entries nearing expiry on read | Memory (never expires under load) |

This is the crux of cache *correctness*, and CacheScope lets you flip it live and watch the next
read's `X-Served-By` change.

## 4.9 The measurement boundary (say this in interviews)

| Layer | Can the origin server measure it? | How it's shown |
|---|---|---|
| L0 Cloudflare | ❌ (hit never reaches origin) | pulled from Cloudflare Analytics API (aggregate) |
| L1 Browser | ❌ (hit never leaves browser) | client-side Resource Timing probe (per browser) |
| L2 / L3 / L4 | ✅ (in the request path) | measured directly, live, in the pipeline |

Understanding *why* L0/L1 can't be measured from the origin — and solving it correctly rather than
faking it — is the most sophisticated part of this project.

---

**Next:** [Chapter 05](05-request-lifecycle.md) traces one request through every one of these layers
and all the way to the live dashboard, with full diagrams.
