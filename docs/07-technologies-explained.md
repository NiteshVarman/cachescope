# 07 · Technologies Explained (from first principles)

← [06 · Code Walkthrough](06-code-walkthrough.md) · [Wiki home](README.md) · Next → [08 · Concurrency & Internals](08-concurrency-and-internals.md)

---

Each technology follows the same shape: **What? · Why do we need it? · Why chosen? · How does it work? · How used here? · What if removed?**

## 7.1 .NET & C#

- **What:** C# is the backend language; **.NET** is the runtime + library platform that runs it.
- **Why needed:** we need a language + runtime to write the server. .NET compiles C# to an
  intermediate form that the runtime executes, handles memory automatically via a **garbage
  collector** (frees unused objects so you don't manage memory by hand), and ships huge built-in
  libraries.
- **Why chosen:** it's a mature, fast, strongly-typed platform ideal for high-concurrency servers,
  with first-class async I/O and the target enterprise ecosystem (.NET + Angular).
- **How it works:** you build the project into a `.dll`; the `dotnet` runtime loads and runs it as a
  process. `async`/`await` (Chapter 08) lets one thread serve many requests.
- **Here:** everything under `src/` is C# on **.NET 10**.
- **If removed:** there's no backend.

## 7.2 ASP.NET Core

- **What:** the .NET framework for building web servers/APIs. Includes **Kestrel** (the actual HTTP
  server), a **middleware pipeline**, **dependency injection**, routing, and more.
- **Why needed:** turning raw TCP bytes into "call this C# method for `GET /api/products/42`" is a
  lot of work; ASP.NET Core does it.
- **Why chosen:** it's the standard, high-performance .NET web stack, with **minimal APIs** (define
  endpoints in a few lines) and built-in DI/health-checks/CORS.
- **How it works — the middleware pipeline:** a request passes through an ordered chain of
  **middleware** (small components), each able to act before/after the next. CacheScope's chain:
  CORS → `CorrelationIdMiddleware` → routing → endpoint. **Minimal APIs** map a URL+method to a
  handler function (`app.MapGet("/api/products/{id}", ...)`).
- **Here:** `CacheScope.Host` is an ASP.NET Core app; `Program.cs` configures it; the `Endpoints/`
  files are minimal-API handlers.
- **If removed:** you'd hand-write an HTTP server — thousands of lines of undifferentiated work.

## 7.3 Entity Framework Core (EF Core)

- **What:** an **ORM** (Object-Relational Mapper) — lets you query the database using C# objects/LINQ
  instead of raw SQL.
- **Why needed:** to read/write the `Products` table without hand-writing and hand-maintaining SQL
  strings, and to keep the schema in code.
- **Why chosen:** it's the standard .NET ORM, with **migrations** (versioned schema) and **seeding**.
- **How it works:** you define a `DbContext` with `DbSet<Product>`. `db.Products.FirstOrDefaultAsync(p => p.Id == 42)`
  is translated to `SELECT ... WHERE Id = 42`. **Migrations** are generated schema-change scripts;
  `MigrateAsync()` applies them so the DB matches your model. **Indexes** (the primary key here) make
  lookups fast.
- **Here:** `CacheScope.Database` — the `CacheScopeDbContext`, `ProductStore`, the `InitialCreate`
  migration, and `HasData` seeding of 100 products.
- **If removed:** you'd write and maintain raw SQL + manual schema management.

## 7.4 Redis (via StackExchange.Redis)

- **What:** an **in-memory key-value store** running as a separate server; the L3 distributed cache.
- **Why needed:** a cache that's **shared** across app instances and **survives** app restarts —
  which the in-process L2 cache can't be.
- **Why chosen:** the de-facto distributed cache — extremely fast, simple key-value model, mature
  .NET client (`StackExchange.Redis`).
- **How it works:** it keeps data in RAM as keys→values; you talk to it over TCP with commands
  (`GET`, `SET key val EX ttl`, `DEL`, `SCAN`). It's **single-threaded**, so each command is atomic.
  The .NET client uses one shared, thread-safe **connection multiplexer** that pipelines all commands.
  Because it's a separate process, values must be **serialized** to JSON to be stored (Chapter 02/04).
- **Here:** `CacheScope.RedisCache`; keys namespaced `cachescope:product:{id}`; run as a container
  (local + cloud) with persistence disabled (pure cache).
- **If removed:** no shared/durable cache; more load on the DB; no L3.

## 7.5 SignalR

- **What:** a .NET library for **realtime** server↔client messaging, usually over **WebSockets**.
- **Why needed:** the dashboard must receive thousands of live updates; the server must **push** them
  (polling would be wasteful and laggy).
- **Why chosen:** it's the standard .NET realtime library — handles transport negotiation, automatic
  reconnection, and message framing so you don't hand-roll WebSockets.
- **How it works:** clients connect to a **hub** (`/hubs/traces`); the server calls named client
  methods (`Clients.All.SendAsync("ReceiveTraces", batch)`) which the client has handlers for. A
  persistent WebSocket keeps the channel open.
- **Here:** `CacheScope.Realtime` — `TraceHub`, the sinks, and the `TraceBroadcastService` that
  batches/pushes traces, stats, and timeline points.
- **If removed:** the dashboard couldn't update live; you'd fall back to polling.

## 7.6 OpenTelemetry & Application Insights

- **What:** **OpenTelemetry (OTel)** is a vendor-neutral standard for emitting **traces** (timed
  operation trees), **metrics**, and **logs**. **Application Insights** is Azure's service that stores
  and visualizes them.
- **Why needed:** to see *inside* production — what each request did, per layer, and how long.
- **Why chosen:** OTel is the industry standard (instrument once, export anywhere); App Insights is
  the native Azure sink that speaks OTel.
- **How it works:** in .NET, OTel is built on `System.Diagnostics.Activity` — an `ActivitySource` is
  a tracer and an `Activity` is a **span** (one timed unit with a parent). `AddSource("CacheScope")`
  subscribes the SDK to our spans; instrumentation auto-creates a server span per request; we open
  child spans (`cache.memory` / `cache.redis` / `cache.database`). Spans are batched and exported to
  App Insights, where the server span becomes a `requests` row and the cache spans become
  `dependencies`, stitched by a shared trace id into an end-to-end view.
- **Here:** configured in `Program.cs`; spans opened in `ProductCacheService`; exported only when an
  App Insights connection string is present (so dev runs offline).
- **If removed:** no external, correlated production visibility.

## 7.7 Docker (images & containers)

- **What:** a tool to **package an app + all its dependencies** into a portable **image**, run as a
  **container** (Chapter 02).
- **Why needed:** to run the exact same artifact on a laptop and in the cloud — no "works on my
  machine."
- **Why chosen:** the universal container standard; Azure Container Apps runs Docker images.
- **How it works:** a **Dockerfile** (recipe) builds an **image** (sealed box) that's pushed to a
  **registry**; a host **runs** the image as a container. **Multi-stage** builds use a big
  build-time image to compile and a small runtime image to run.
- **Here:** `src/Dockerfile` (multi-stage: .NET SDK builds → slim ASP.NET runtime runs);
  `docker-compose.yml` runs Redis + SQL + Host together for local dev.
- **If removed:** inconsistent environments + manual dependency installs everywhere.

## 7.8 Bicep (Infrastructure as Code)

- **What:** a language for **declaring Azure infrastructure as code** — you describe the resources you
  want and Azure creates them.
- **Why needed:** so the entire cloud environment is reproducible and version-controlled, not clicked
  together by hand ("click-ops") which drifts and can't be reproduced.
- **Why chosen:** Bicep is Azure's native IaC language (cleaner than raw ARM JSON).
- **How it works:** you write `main.bicep` declaring resources (Container Apps env, Redis app, SQL,
  App Insights); `az deployment group create` makes Azure converge to that description.
- **Here:** `infra/main.bicep` provisions the whole topology; `deploy.sh`/`teardown.sh` wrap it.
- **If removed:** manual, error-prone, non-reproducible cloud setup.

## 7.9 Azure services

**Azure** is the cloud provider (Chapter 02). CacheScope uses:
- **Azure Container Apps** — runs container **images** as managed, long-lived services with HTTPS
  ingress, TLS certs, custom domains, and revisions. Hosts the API container *and* the Redis
  container. *Why (not Azure Functions):* Functions are short-lived/stateless and scale per-invocation;
  CacheScope holds in-memory state, runs background services, and serves a persistent WebSocket — it
  needs a long-lived single service.
- **Azure SQL Database** — a managed relational database (L4); serverless with auto-pause.
- **Application Insights + Log Analytics** — telemetry store (7.6).
- **(Registry)** the image is stored in **GitHub Container Registry**, which Azure pulls from.

Full resource-by-resource explanation is in [Chapter 10](10-deployment.md).

## 7.10 Cloudflare

- **What:** a CDN + DNS + edge platform. Here it does **five** jobs: authoritative **DNS** for
  `cachescope.dev`, a **reverse proxy** through its edge, the **edge cache (L0)**, **TLS**
  termination, and **hosting the Angular frontend** (as static files). It also exposes a **GraphQL
  Analytics API** (source of the L0 stats) and a **cache-purge API**.
- **Why needed:** global low latency + a real L0 layer + free HTTPS + a home for the SPA.
- **Why chosen:** one platform collapses DNS + CDN + TLS + static hosting + analytics.
- **How it works:** the domain's nameservers point to Cloudflare, so it controls DNS and can proxy
  traffic through its global edge, caching `GET /api/products/*` per a Cache Rule and serving the SPA.
- **Here:** covered end-to-end in [Chapter 10](10-deployment.md).
- **If removed:** no L0, no global edge, and you'd need separate DNS/TLS/static-hosting.

## 7.11 GitHub Actions (CI/CD)

- **What:** GitHub's **CI/CD** (Continuous Integration / Continuous Deployment) system — runs
  automated workflows on events like a push.
- **Why needed:** so tests run and the app deploys automatically and reliably on every change, instead
  of manual steps.
- **How it works:** `.github/workflows/deploy.yml` defines jobs. On push to `main`: run tests → build
  the image → push to GHCR → tell Azure Container Apps to roll out the new image. Authenticates to
  Azure via **OIDC** (short-lived, passwordless trust — no stored secrets).
- **Here:** the backend pipeline; the frontend auto-deploys separately via Cloudflare's Git
  integration.
- **If removed:** manual, error-prone deploys with no test gate.

## 7.12 Angular + Angular Material + TypeScript (frontend)

- **What:** **Angular** is the browser-app framework; **TypeScript** the language; **Angular
  Material** the UI component library. Together they build the dashboard SPA.
- **Why chosen:** Angular's **signals** (fine-grained reactive state) suit a high-frequency live
  dashboard; Material gives production-quality components cheaply.
- **How it works:** TypeScript/Angular compile to static HTML/JS/CSS the browser runs; **signals**
  hold reactive state (`traces`, `stats`) and the UI re-renders when they change; `@microsoft/signalr`
  is the client that receives server pushes.
- **Here:** `web/`; detailed in [Chapter 09](09-ui-features.md).
- **If removed:** no dashboard — the whole point (visibility) disappears.

---

**Next:** [Chapter 08](08-concurrency-and-internals.md) — the concurrency machinery that makes all of
this correct and fast: async/await, DI lifetimes, Channels, single-flight, and thread safety.
