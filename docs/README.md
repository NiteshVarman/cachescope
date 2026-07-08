# CacheScope — Engineering Wiki

> The single source of truth for the CacheScope project. If you are a brand-new engineer
> assigned to this codebase, **read this wiki front to back** and you will understand the
> entire system without asking anyone. Nothing here assumes prior knowledge of caching,
> ASP.NET Core, Redis, Docker, Azure, or Cloudflare — every concept is explained from first
> principles, and every important line of code is walked through.

## How this wiki is organized

Read the chapters in order. Each one builds on the last.

| # | Chapter | What you'll learn |
|---|---------|-------------------|
| 01 | [Introduction & the Business Problem](01-introduction-and-business-problem.md) | What CacheScope is, the real-world problem it models, and why it exists. |
| 02 | [Foundational Concepts (from zero)](02-foundational-concepts.md) | Processes, client/server, HTTP, latency, caching, cloud, containers, databases — every keyword defined before we use it. |
| 03 | [System Architecture](03-system-architecture.md) | The modular monolith, the 11 projects, the dependency graph, and *why* it's shaped this way. |
| 04 | [The Five Cache Layers](04-the-five-cache-layers.md) | L0–L4 in depth: where each stores data, how it's measured, and why the hierarchy exists. |
| 05 | [The Request Lifecycle](05-request-lifecycle.md) | A single request traced end-to-end with diagrams, plus the realtime broadcast path. |
| 06 | [Code Walkthrough — Project by Project](06-code-walkthrough.md) | Every project, every important file and class, explained. |
| 07 | [Technologies Explained](07-technologies-explained.md) | .NET, ASP.NET Core, EF Core, Redis, SignalR, OpenTelemetry, Docker, Bicep, Azure, Cloudflare — each from first principles. |
| 08 | [Concurrency & Internals](08-concurrency-and-internals.md) | async/await, DI lifetimes, Channels, single-flight, backpressure, thread safety. |
| 09 | [The UI, Feature by Feature](09-ui-features.md) | Every dashboard panel: what it does, how it works, and the code behind it. |
| 10 | [Deployment — Local to cachescope.dev](10-deployment.md) | Every service, every step, from `docker compose up` to a live public URL. |
| 11 | [Operations Runbook](11-operations-runbook.md) | How to debug, common production issues we actually hit, and how to modify safely. |
| 12 | [Glossary](12-glossary.md) | Every term, one place. |

## The 30-second summary (so the rest has context)

CacheScope is a **web application that makes multi-layer caching visible**. A "cache" is a fast
place to keep a copy of data so you don't have to fetch it from a slow place every time. Real
systems stack several caches in front of their database. CacheScope lets you send traffic through
that stack and **watch, in real time, which layer answered each request and how long it took** —
plus simulate failure modes (like a "cache stampede") and switch caching strategies live.

It is deployed and live at **https://cachescope.dev**.

```
        You (browser)
             │  https
             ▼
   ┌──────────────────┐     ┌───────────────────────────────────────────────┐
   │  Cloudflare (L0) │     │            Azure Container Apps                 │
   │  global edge     │────▶│  ASP.NET Core API  ── IMemoryCache (L2)         │
   │  + hosts the UI  │     │        │                                        │
   └──────────────────┘     │        ├──▶ Redis container (L3)                │
             ▲              │        └──▶ Azure SQL (L4, source of truth)     │
             │ WebSocket    │  SignalR pushes live traces to the dashboard    │
   Angular dashboard ◀──────┤  OpenTelemetry pushes traces to App Insights    │
                            └───────────────────────────────────────────────┘
```

## Conventions used in this wiki

- **Every keyword is defined the first time it appears**, and again in the [Glossary](12-glossary.md).
- Diagrams are ASCII so they render everywhere.
- Code references point to real files, e.g. [`ProductCacheService`](../src/CacheScope.Api/Caching/ProductCacheService.cs).
- Each concept follows the same shape: **What is it? · Why do we need it? · Why this choice? · How does it work? · How is it used here? · What if we removed it?**
