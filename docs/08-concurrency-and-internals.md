# 08 · Concurrency & Internals

← [07 · Technologies](07-technologies-explained.md) · [Wiki home](README.md) · Next → [09 · UI Features](09-ui-features.md)

---

A web server does many things at once. This chapter explains the concurrency machinery CacheScope
uses to stay both **correct** (no data races) and **fast** (no blocked threads), from first
principles.

## 8.1 `async` / `await` — and why synchronous code would be worse

- **What:** `async` marks a method that may pause; `await` means "pause here until this finishes, but
  **release the thread** to do other work while waiting."
- **Why it matters:** reading Redis or SQL involves *waiting on the network/disk* — the CPU has
  nothing to do during that wait. **Synchronous** code would **block the thread** (it sits idle,
  unusable). With `async`, the thread is returned to the **thread pool** (a shared set of worker
  threads) and can serve other requests; when the I/O completes, the method resumes.

```
  Synchronous (bad):   Thread ──[ waiting 40ms on DB ... idle, wasted ]──▶ done
  Asynchronous (good): Thread ──await DB──▶ (thread freed, serves others) ──resume──▶ done
```

- **The payoff:** a few dozen threads can serve thousands of concurrent requests. Blocking would need
  thousands of threads (huge memory + context-switching cost) and would collapse under load.
- **Here:** every I/O call (`redis.GetAsync`, `store.GetByIdAsync`, `SendAsync`) is `await`ed. That's
  why the pipeline scales.
- **`Task<T>`:** an `async` method returns a `Task<T>` — a "promise of a future value" you `await`.

## 8.2 `AsyncLocal` and why spans survive `await`

- **What:** `AsyncLocal<T>` is a value that follows the *logical* asynchronous flow rather than one
  thread. Because `await` can resume on a *different* thread, ordinary thread-local state would be
  lost — but `AsyncLocal` rides on the **ExecutionContext** that async/await captures and restores.
- **Why it matters here:** `Activity.Current` (the current OpenTelemetry span, Chapter 07) is stored
  in `AsyncLocal`. That's why `using (activitySource.StartActivity("cache.redis")) { await redis... }`
  correctly times the whole awaited operation even across thread hops — the span is still "current"
  when the continuation resumes.

## 8.3 Dependency Injection lifetimes (and the captive-dependency trap)

The DI container ([3.5](03-system-architecture.md)) can create objects with three **lifetimes**:

- **Singleton** — one instance for the whole process. Use for shared state or expensive resources.
  Here: `ICachePolicy`, the memory/Redis layers, `IConnectionMultiplexer`, `IDatabaseMetrics`,
  `IRequestCounter`, `ILiveStats`, `IMetricsTimeline`, `IRequestDetailStore`, `SingleFlight`,
  `RefreshAheadScheduler`, `StampedeRunner`, `TrafficRunner`, the trace sinks.
- **Scoped** — one instance per request (per DI **scope**). Here: `CacheScopeDbContext`,
  `IProductStore`, `IProductCacheService`, `IRequestExecutor`, `ITrafficSupport`, `ICacheOperations`.
  These hang off the request/DbContext.
- **Transient** — a new instance every time it's requested. Here: the typed `HttpClient`s.

```
  Request A ──▶ [ Scope A ]  new DbContext, new ProductStore, new ProductCacheService
                    │  (these also USE singletons: memory, redis, policy, ...)
  Request B ──▶ [ Scope B ]  its own DbContext/Store/Service, same singletons
```

**The captive-dependency rule (critical):** a **scoped** service may depend on a **singleton**, but a
**singleton must never capture a scoped** service — the singleton would hold onto one request's
DbContext forever, corrupting later requests. This is why `RefreshAheadScheduler`, `StampedeRunner`,
and `WriteBehindFlusher` (all singletons) **do not inject** `IProductStore` (scoped); instead they
inject `IServiceScopeFactory` and create a fresh scope for each background operation:

```csharp
using var scope = scopeFactory.CreateScope();
var store = scope.ServiceProvider.GetRequiredService<IProductStore>();  // a fresh, valid scoped store
```

## 8.4 Hosted services (background work)

- **What:** a `BackgroundService` (a "hosted service") is a long-running task .NET starts at boot and
  runs for the app's lifetime.
- **Here:** `TraceBroadcastService` (drains the trace channel + broadcasts) and `WriteBehindFlusher`
  (persists queued writes) and `CloudflareEdgePoller` (refreshes L0 stats). They run *off* the request
  path so requests never wait on them.

## 8.5 Channels & backpressure (the realtime buffer)

- **What:** `System.Threading.Channels` is a thread-safe in-memory **producer/consumer queue**. Many
  producers write; one (or more) consumers read.
- **Why here:** the request threads (producers) must hand traces to the single broadcaster (consumer)
  **without blocking**. A channel is the buffer between them.
- **Bounded + `DropOldest`:** the channel has a capacity; when full it **drops the oldest** queued
  trace rather than blocking producers or growing memory forever. This is **backpressure**: under
  extreme load the *stream* degrades gracefully (a few dropped visual rows) while the request path and
  memory stay safe. The **stats** are recorded synchronously (not through the channel), so they stay
  exact regardless.

```
  request threads ──TryWrite (never blocks)──▶ [ bounded Channel, drop-oldest ] ──drain──▶ broadcaster
```

## 8.6 Single-flight (stampede protection), line by line

`SingleFlight` collapses a herd of concurrent misses for the *same* key into **one** DB load:

```csharp
private readonly ConcurrentDictionary<string, Lazy<Task<Product?>>> _inflight = new();

public Task<Product?> RunAsync(string key, Func<Task<Product?>> load)
{
    var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<Product?>>(() => LoadAndEvictAsync(key, load)));
    return lazy.Value;   // all concurrent callers get the SAME Task
}
```

- **`ConcurrentDictionary`** — a thread-safe dictionary many threads use at once.
- **`Lazy<Task<...>>`** — the crucial detail. `ConcurrentDictionary.GetOrAdd` does *not* guarantee its
  factory runs only once under contention (it can run many times, keeping only one result). If the
  factory *started a DB task*, you'd start many. By storing a **`Lazy`**, the factory only creates a
  cheap wrapper; only the **winning** `Lazy` is stored, and `Lazy` guarantees the thing that actually
  *starts the DB task* (`lazy.Value`) runs **exactly once**. Every caller awaits that same `Task`.
- **`finally { _inflight.TryRemove(key) }`** — evict when the load finishes, so the *next* expiry
  starts a fresh cycle.
- **Singleton** — mandatory; coalescing only works if all requests share one `_inflight` dictionary.

Result: 1000 concurrent misses → **1** DB query. (Proven by the Stampede Demo.)

## 8.7 `Interlocked` — lock-free counters

- **What:** `Interlocked` performs atomic operations (increment, add, compare-exchange) on a number
  without a lock. Two threads incrementing the same counter can't corrupt it.
- **Why:** the stats counters are updated on *every* request from many threads; a lock would be a
  contention bottleneck. `Interlocked` is fast and correct.
- **Here:** `LiveStats`, `DatabaseMetrics`, `RunMetrics`, and `LatencyHistogram` use `Interlocked`
  for all counters (including a compare-exchange loop to track the peak latency).

## 8.8 The `IMemoryCache.Clear()` trick

.NET's `IMemoryCache` has **no** "remove everything" method. `MemoryCacheLayer` implements `Clear()`
by linking every entry to a shared `CancellationTokenSource` via a `CancellationChangeToken`:

```csharp
entryOptions.AddExpirationToken(new CancellationChangeToken(cts.Token));  // on every Set
// Clear():
old = _resetCts; _resetCts = new CancellationTokenSource();  // swap in a fresh token for future entries
old.Cancel();                                                // cancelling evicts all linked entries at once
```

Cancelling the token expires every entry that was linked to it — an atomic "clear all." A new token
source is installed so future entries link to the new one.

## 8.9 Streaming percentiles without sorting

Computing P95/P99 naively means sorting all latencies — O(n log n) per query and unbounded memory.
`LatencyHistogram` instead keeps **fixed buckets** (e.g. ≤1ms, ≤2ms, ≤4ms, …). Recording a latency is
one `Interlocked` increment of the right bucket (O(1), constant memory). A percentile is computed by
walking the cumulative counts to the target and interpolating within the bucket. This is why the
analytics stay cheap even under heavy traffic.

## 8.10 Thread-safety summary

| State | Made safe by |
|---|---|
| Stats/metrics counters | `Interlocked` (lock-free) |
| Cache policy | a `Lock` in `CachePolicyState` |
| Single-flight map | `ConcurrentDictionary` + `Lazy` |
| Redis connection | the thread-safe `IConnectionMultiplexer` (shared singleton) |
| Trace stream buffer | a thread-safe `Channel` (bounded, drop-oldest) |
| Background DB work from singletons | fresh scope per op via `IServiceScopeFactory` |
| Span context across `await` | `AsyncLocal` / ExecutionContext |

---

**Next:** [Chapter 09](09-ui-features.md) — every dashboard feature, what it does, and the code
behind it.
