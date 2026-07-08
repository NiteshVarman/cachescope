# 02 · Foundational Concepts (from zero)

← [01 · Introduction](01-introduction-and-business-problem.md) · [Wiki home](README.md) · Next → [03 · Architecture](03-system-architecture.md)

---

This chapter assumes **no** prior knowledge. Every term used elsewhere in the wiki is defined here.
If you already know a section, skip it. Terms are also collected in the [Glossary](12-glossary.md).

## 2.1 A program, a process, RAM, and a thread

- A **program** is a file on disk containing instructions (e.g. `CacheScope.Host.dll`).
- When you *run* a program, the operating system loads it into **RAM** (Random Access Memory — the
  computer's fast, temporary working memory) and starts executing it. A running program is called a
  **process**. A process has its own private chunk of RAM that other processes cannot see.
- **RAM vs disk:** RAM is fast (nanoseconds) but **volatile** — everything in it disappears when the
  process stops or the machine restarts. **Disk** (an SSD/hard drive) is slower (milliseconds) but
  **persistent** — data survives restarts. This distinction is the *entire reason* caching layers
  differ: memory caches are fast but vanish on restart; the database is slow but permanent.
- A **thread** is a single sequence of execution within a process. A process can have many threads
  running "at the same time," letting it do multiple things at once (e.g. handle many users). A web
  server uses a **pool** of threads to serve many requests concurrently.

```
   Machine (physical or virtual)
   ┌───────────────────────────────────────────┐
   │  RAM                                        │
   │  ┌──────────────────┐  ┌────────────────┐   │
   │  │ Process:          │  │ Process:        │  │   Each process's memory is
   │  │ CacheScope.Host   │  │ Redis           │  │   private — they talk only
   │  │  ├ thread 1        │  │  ├ thread ...   │  │   over the network, not by
   │  │  ├ thread 2        │  │                 │  │   reading each other's RAM.
   │  │  └ thread ...      │  │                 │  │
   │  └──────────────────┘  └────────────────┘   │
   └───────────────────────────────────────────┘
```

**Why this matters for CacheScope:** L2 (memory cache) lives *inside* the Host process's RAM, so it
can hold real C# objects and is blazing fast — but it's private to that process and gone on restart.
L3 (Redis) is a *separate* process, so data must be copied over the network to reach it (slower) but
it's shared and survives Host restarts.

## 2.2 Client and server

- A **server** is a program that waits for requests and responds to them (e.g. our ASP.NET Core
  app). It runs continuously.
- A **client** is a program that *sends* requests (e.g. your web browser, or the Angular app).
- The **client–server model**: client asks, server answers.

```
   CLIENT                          SERVER
   (browser) ── "give me /x" ────▶ (waits, does work)
             ◀──── "here is x" ─── (responds)
```

## 2.3 Networks, IP, ports, DNS

- Computers talk over a **network**. Each machine has an **IP address** (like a postal address, e.g.
  `4.247.232.75`). A **port** is a numbered "door" on a machine (e.g. `443` for secure web, `6379`
  for Redis, `1433` for SQL Server) so one machine can run many services.
- Humans don't want to type IP addresses, so **DNS** (Domain Name System) is the internet's phone
  book: it translates a **domain name** like `api.cachescope.dev` into an IP address. When you type
  a domain, your computer asks a DNS server "what IP is this?" and then connects to that IP.
- **TCP** is the protocol that creates a reliable, ordered connection between two machines (like a
  phone call that guarantees words arrive in order). **TLS** (a.k.a. SSL) encrypts that connection so
  eavesdroppers can't read it — this is the "S" (secure) in **HTTPS**.

```
  api.cachescope.dev  ──DNS lookup──▶  104.x.x.x (Cloudflare)  ──TCP+TLS──▶  encrypted connection
```

## 2.4 HTTP — the language of the web

**HTTP** (HyperText Transfer Protocol) is the set of rules browsers and web servers use to talk. It
is **request/response**: the client sends a **request**, the server sends back a **response**.

An HTTP **request** has:
- a **method** (the verb): `GET` (read something), `POST` (create/do something), `PUT` (update),
  `DELETE` (remove). CacheScope reads products with `GET` and updates them with `PUT`.
- a **path/URL**: e.g. `/api/products/42`.
- **headers**: metadata key–value pairs, e.g. `Origin: https://cachescope.dev`, or
  `Cache-Control: max-age=30`.
- an optional **body**: data sent with the request (used by POST/PUT), usually JSON.

An HTTP **response** has:
- a **status code**: a number saying what happened — `200 OK` (success), `304 Not Modified`
  (your cached copy is still fine), `404 Not Found`, `500 Internal Server Error` (server broke).
- **headers**: e.g. `Cache-Control`, `ETag`, `X-Served-By: Memory`.
- a **body**: the data (usually JSON).

```
  REQUEST                                  RESPONSE
  GET /api/products/42                     200 OK
  Host: api.cachescope.dev                 Content-Type: application/json
  Origin: https://cachescope.dev           Cache-Control: public, max-age=30
                                           ETag: W/"42-v1"
                                           X-Served-By: Memory
                                           {"id":42,"name":"Product 042",...}
```

Two HTTP concepts CacheScope leans on heavily:
- **`Cache-Control` header** — the server telling clients/CDNs "you may cache this response for N
  seconds." This is how L0 (Cloudflare) and L1 (browser) know they're allowed to keep a copy.
- **`ETag` + `304 Not Modified`** — the server gives each response a version tag (`ETag`); the client
  can later ask "has it changed since `ETag` X?" and the server can answer `304` (no body) meaning
  "use your copy." This is cheap **revalidation**.

## 2.5 JSON and serialization

- **JSON** (JavaScript Object Notation) is a simple text format for structured data:
  `{"id":42,"name":"Product 042","price":152.00}`. It's the lingua franca of web APIs because it's
  human-readable and every language can parse it.
- **Serialization** = converting an in-memory object (which only makes sense inside one process) into
  a portable format like a JSON string (bytes that can be sent over a network or stored). **Deserialization**
  is the reverse. This matters enormously for Redis (Chapter 04/07): because Redis is a separate
  process, a C# `Product` object cannot be "sent" to it — it must first be **serialized** to a JSON
  string, then **deserialized** back into a `Product` when read.

## 2.6 Latency, throughput, and why layers differ

- **Latency** = how long one operation takes (e.g. 0.001 ms for a memory read, 40 ms for a database
  query). Lower is better.
- **Throughput** = how many operations per second the system can handle. Higher is better.
- Rough latency ladder in this project:

```
   L2 memory read     ~0.001 ms   (an in-RAM object reference — no copy, no network)
   L3 Redis read      ~1–5 ms     (network round-trip + JSON deserialize)
   L4 database query   ~10–40 ms  (network + disk + query engine)
```

A memory hit is roughly **10,000× faster** than a DB read. That gap is *why* we bother with layers.
But the **bigger** win is throughput/load: every cache hit is a DB query prevented, so the database
stays idle enough to survive traffic spikes.

## 2.7 Databases, SQL, tables, queries, indexes

- A **database** is a program that stores data **persistently** (on disk) and lets you query it
  reliably. CacheScope uses a **relational database** (Azure SQL / SQL Server).
- Data lives in **tables** (like spreadsheets): rows and columns. CacheScope has a `Products` table
  with columns `Id, Name, Category, Price, Stock, Version, UpdatedAt`.
- **SQL** (Structured Query Language) is how you ask a database for data:
  `SELECT * FROM Products WHERE Id = 42`. That's a **query**.
- An **index** is a data structure the database keeps so it can find rows fast (like a book's index).
  The `Id` column is the **primary key** and is automatically indexed, so `WHERE Id = 42` is instant.
  Without an index, the DB would scan every row.
- **ORM** (Object-Relational Mapper): a library that lets you query the database using your
  programming language's objects instead of writing raw SQL strings. CacheScope uses **Entity
  Framework Core** (Chapter 07), so `db.Products.FirstOrDefaultAsync(p => p.Id == 42)` becomes the
  SQL above.

## 2.8 In-memory vs. distributed cache (L2 vs L3)

- An **in-memory cache** lives inside one process's RAM. Fastest possible, but private to that
  process and lost on restart. In .NET this is `IMemoryCache`. → **L2**.
- A **distributed cache** is a *separate* cache server (like **Redis**) that multiple app instances
  share and that survives app restarts. Slower than in-memory (network + serialization) but shared
  and durable. → **L3**.

```
   Two app instances, one shared Redis:
   [App A: its own L2] ─┐
                        ├──▶ [Redis L3, shared]
   [App B: its own L2] ─┘
```

## 2.9 Cache vocabulary (hit, miss, TTL, eviction, invalidation)

- **Hit** — the data was found in the cache (fast). **Miss** — it wasn't; you must go to the slower
  layer.
- **TTL** (Time To Live) — how long a cached copy is allowed to live before it's considered expired.
  Bounds staleness and memory use.
- **Eviction** — removing an entry from the cache (because its TTL expired, or memory is full).
- **Invalidation** — deliberately removing/replacing a cached copy because the underlying data
  changed (a *write* happened). The core correctness problem of caching.
- **Cache-aside** — the pattern where the application checks the cache first, and on a miss loads
  from the DB and *populates* the cache itself. This is CacheScope's default read pattern.
- **Cache stampede / thundering herd** — many concurrent requests all missing the same key at once
  and flooding the DB. Fixed by **single-flight** (make only one of them do the load; the rest wait).

## 2.10 Cloud computing (from zero)

- **The cloud** just means *renting computers and services over the internet* from a provider
  instead of buying and running your own physical servers. CacheScope's provider is **Microsoft
  Azure**.
- Instead of "buy a server, install an OS, plug it in," you click/configure and the provider gives
  you a running service. You pay for what you use, and the provider handles the hardware, power,
  networking, and much of the maintenance.
- **PaaS vs. self-hosted:** a **managed service** (PaaS, Platform-as-a-Service) is one the provider
  operates for you (e.g. **Azure SQL Database** — you get a database, Microsoft runs it). A
  **self-hosted** service is one you run yourself inside a container you control (e.g. CacheScope
  runs **Redis** as its own container rather than using a managed Redis service).
- **CDN** (Content Delivery Network) — a global network of servers near users that caches content at
  the "edge" (close to users) so responses come from a nearby city instead of a distant origin
  server. **Cloudflare** is CacheScope's CDN, and its edge cache is **L0**.

## 2.11 Virtualization, containers, images (Docker)

**The problem:** "it works on my machine" — software behaves differently on different computers
because of different OS versions, installed libraries, etc.

**The fix — containers:** A **container** packages an application *together with everything it needs
to run* (runtime, libraries, config) into one isolated unit that runs identically everywhere.

- A **container image** is the *blueprint* — a read-only, self-contained bundle ("the app + its
  dependencies"). Think of it as a sealed box.
- A **container** is a *running instance* of an image (the box, opened and running).
- **Docker** is the most common tool for building and running containers.
- A **registry** is a shared shelf where images are stored and pulled from (CacheScope uses **GitHub
  Container Registry / GHCR**).
- A **Dockerfile** is the recipe that describes how to build an image.

```
   Dockerfile (recipe)  ──build──▶  Image (sealed box, in a registry)  ──run──▶  Container (running)
```

**Why CacheScope uses this:** the API is built into a Docker image, pushed to GHCR, and Azure runs
that image as a container. The *exact same image* runs on a laptop (via `docker compose`) and in the
cloud — no "works on my machine." Redis and SQL Server also run as containers locally.

## 2.12 What .NET and C# are

- **C#** is the programming language the CacheScope backend is written in (like Java, but Microsoft's).
- **.NET** is the **runtime** and library ecosystem that runs C# programs — it turns your C# into a
  running process, manages memory (via a **garbage collector** that automatically frees unused
  objects), and provides huge built-in libraries. CacheScope targets **.NET 10**.
- **ASP.NET Core** is the part of .NET for building web servers/APIs (Chapter 07).

## 2.13 What Angular and TypeScript are (the frontend)

- The **frontend** is the part that runs in your **browser** (the UI you see and click).
- **TypeScript** is JavaScript with types — the language the frontend is written in.
- **Angular** is a framework for building browser applications (the dashboard). It's compiled into
  plain HTML/CSS/JavaScript that the browser downloads and runs.
- A **SPA** (Single-Page Application) is a web app that loads once and then updates the page
  dynamically without full reloads. CacheScope's dashboard is a SPA.

## 2.14 WebSockets and realtime push

Normal HTTP is one-shot: the client asks, the server answers, done. But CacheScope's dashboard needs
the **server to push** thousands of live updates to the browser. For that it uses a **WebSocket** — a
*persistent, two-way* connection that stays open so the server can send messages any time. The
library that manages this is **SignalR** (Chapter 07). Contrast with **polling** (the client asking
"any updates?" over and over), which is wasteful and laggy.

```
  Polling:   client ──"anything?"──▶ server  (repeat every second, mostly "no")
  WebSocket: client ══open connection══ server ──push!──▶ ──push!──▶ ──push!──▶
```

---

You now have the vocabulary. **Next:** [Chapter 03](03-system-architecture.md) uses these terms to
explain how CacheScope is structured into projects and why.
