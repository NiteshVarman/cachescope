# 01 · Introduction & the Business Problem

← [Wiki home](README.md) · Next → [02 · Foundational Concepts](02-foundational-concepts.md)

---

## 1.1 What is CacheScope, in one paragraph?

CacheScope is a **web application** — a program you use through a web browser — whose job is to
**demonstrate, measure, and simulate how "caching" works across multiple layers**. It is *not* a
shopping site or a to-do app; the little "products" it stores are just something to fetch so the
caching behaviour has data to act on. The real product is the **observability**: you can generate
traffic and watch, live, *where each request was answered* and *how fast*, and you can trigger
classic caching failure modes and fixes on demand.

If you take nothing else away: **CacheScope makes an invisible thing — where a request got its data
from — visible and measurable.**

## 1.2 First, what is "caching"? (from zero)

Imagine you are cooking and you need salt. The salt lives in a warehouse across town (slow to get).
The first time, you drive to the warehouse. But you're smart: you bring back a big bag and keep a
small pinch in a **little bowl right next to the stove**. Now, every time you need salt, you reach
into the bowl — instant — and only drive to the warehouse again when the bowl runs out.

That little bowl is a **cache**: a small, fast, *nearby* copy of something that normally lives
somewhere slow and far. Caching is the general technique of *"keep a copy of frequently-used data
somewhere fast so you rarely have to go to the slow source."*

- The **slow source** here is a **database** (a program that permanently stores data on disk — see
  [Chapter 02](02-foundational-concepts.md)).
- The **fast copies** are caches.

**Why does this work at all?** Because of a deep truth about real systems: **most data is read far
more often than it changes.** A product's price might change once a day but be *read* thousands of
times a second. So asking the slow database every single time is enormous waste. Keep a copy.

## 1.3 The business problem (why companies care)

In real companies, the database is usually the **most expensive and hardest-to-scale** part of the
system. You can add more web servers cheaply, but databases are heavy: they store data safely on
disk, guarantee correctness, and don't like thousands of simultaneous requests. If every user
request hit the database directly, the database would become the **bottleneck** — the single slow
part that limits the whole system — and under a traffic spike it would fall over, taking the site
down.

Caching is the standard defense. Every request a cache answers is a database query that **never
happens**. CacheScope quantifies exactly this with a metric literally called *"queries prevented"*.

But caching is also where some of the **worst production outages** come from, because caching
introduces two hard problems:

1. **Staleness** — if you keep a copy and the original changes, your copy is now *wrong*. Serving a
   wrong (stale) price is a bug. Managing this is called **cache invalidation** (famously "one of
   the two hard things in computer science").
2. **Stampedes** — when a popular cached item expires, *all* the requests that were being served
   from the cache suddenly miss at once and flood the database simultaneously — a **thundering
   herd** that can crash the DB.

CacheScope exists to make a **platform/backend engineering team** understand and tune these
behaviours *before* they cause an incident. Concretely, it models:

| Real-world problem | How CacheScope demonstrates it |
|---|---|
| Latency & DB load | Live per-layer latency + "queries prevented" counter |
| Cache stampede / thundering herd | The Stampede Demo: 1000 concurrent requests → 1000 DB queries vs. 1 with protection |
| Write-consistency trade-offs | Four switchable write strategies (cache-aside / write-through / write-behind / refresh-ahead) |
| Realistic traffic (a few "hot" items dominate) | The Traffic Generator's **Zipf** pattern |
| Capacity planning under load | Configurable RPS / concurrency / patterns |

## 1.4 Who is the "user" of this tool?

Not a shopper. The user is a **backend or platform engineer** who wants to answer questions like:
*"If I add a Redis cache, how many DB queries do I actually save?"*, *"What happens to my database
when my hottest key expires?"*, *"Is write-through worth the extra write cost over cache-aside?"*.
CacheScope lets them *see* the answers instead of guessing.

## 1.5 What "multi-layer" means here

CacheScope doesn't have one cache — it has **five layers**, each faster but smaller/more volatile
than the one below it. A request checks them from fastest to slowest and stops at the first one that
has the data:

```
 fastest, nearest ┌─────────────────────────────┐
                  │ L0  Cloudflare edge (global) │   served worldwide, never reaches your server
                  ├─────────────────────────────┤
                  │ L1  Browser cache           │   on the user's own device
                  ├─────────────────────────────┤
                  │ L2  App memory (IMemoryCache)│   inside the server process's RAM
                  ├─────────────────────────────┤
                  │ L3  Redis (shared cache)    │   a separate cache server
                  ├─────────────────────────────┤
 slowest, truth   │ L4  Database (Azure SQL)    │   the source of truth, on disk
                  └─────────────────────────────┘
```

Each layer is covered in depth in [Chapter 04](04-the-five-cache-layers.md). For now, the key idea:
**the further up a request is answered, the faster it is and the less load reaches the database.**

## 1.6 What CacheScope is *not*

- It is **not** a production caching library you'd drop into another app. It's a *teaching and
  measurement* tool.
- It is **not** trying to be a horizontally-scaled service. It runs as a **single instance** on
  purpose (explained in [Chapter 03](03-system-architecture.md) and
  [Chapter 11](11-operations-runbook.md)) so its live measurements stay coherent.

## 1.7 How the project was built (for context)

It was built in **8 phases**, each adding one capability on top of the last: foundations → the cache
pipeline → realtime + UI → traffic generator → analytics → cache operations/policies → stampede demo
→ observability + hardening. You'll see this reflected in the git history and the code structure.
The phase list is in the project [README](../README.md); the code organization is
[Chapter 03](03-system-architecture.md).

---

**Next:** before we can read any code, you need the vocabulary. [Chapter 02](02-foundational-concepts.md)
defines every foundational term — process, thread, client/server, HTTP, JSON, latency, database,
cloud, container — from scratch.
