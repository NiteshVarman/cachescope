# 10 · Deployment — Local to cachescope.dev

← [09 · UI Features](09-ui-features.md) · [Wiki home](README.md) · Next → [11 · Operations Runbook](11-operations-runbook.md)

---

This chapter explains **every service** involved in shipping CacheScope and the **exact steps** from
a laptop to the live `https://cachescope.dev`. It assumes no prior cloud/Docker knowledge (see
[Chapter 02](02-foundational-concepts.md) and [Chapter 07](07-technologies-explained.md) for the
concepts).

## 10.1 The shipment picture (two pipelines)

There are **two independent deploy pipelines**, both triggered by a `git push`:

```
                              git push to main
                             /                 \
        ┌───────── BACKEND ──────────┐     ┌──────── FRONTEND ────────┐
        │ GitHub Actions             │     │ Cloudflare (Git-connected) │
        │  1. run tests (gate)        │     │  1. npm ci                 │
        │  2. docker build image      │     │  2. ng build               │
        │  3. push image → GHCR        │    │  3. deploy static assets   │
        │  4. az containerapp update  │     │     to Cloudflare edge     │
        └──────────┬──────────────────┘     └───────────┬──────────────┘
                   ▼                                     ▼
        Azure Container Apps (API)             cachescope.dev (the SPA)
```

- **Backend** (the .NET API image) → GitHub Actions → GitHub Container Registry → Azure Container Apps.
- **Frontend** (the Angular SPA) → Cloudflare builds it from the repo and serves it at the edge.

Cloudflare also fronts the API (`api.cachescope.dev`) for DNS, edge caching (L0), and TLS.

## 10.2 Running it locally first

Everything needed to run locally is a container, orchestrated by
[`docker-compose.yml`](../docker-compose.yml):

```bash
# from the repo root — starts Redis (L3) + SQL Server (L4) + the Host (API), wired together
docker compose up --build          # API on http://localhost:5199

# the frontend (in another terminal)
cd web
npm install                        # first time only
npm start                          # dashboard on http://localhost:4200
```

- `docker compose` reads the compose file and starts three containers with health checks and
  dependency ordering (the Host waits for Redis + SQL to be healthy).
- Locally, **both** Redis and SQL are containers (official `redis` and `mssql/server` images from
  Docker Hub). In the cloud, only Redis is a container; SQL is the managed Azure SQL service.
- The Angular dev server (`ng serve`) serves the UI on `:4200` and calls the local API on `:5199`
  (the API's dev CORS allows any localhost origin).

Quick check the pipeline works:
```bash
curl -i http://localhost:5199/health
for i in 1 2 3; do curl -s -D - -o /dev/null http://localhost:5199/api/products/42 | grep -i x-served-by; done
# => Database, then Memory, then Memory
```

## 10.3 The Azure resources (what each one is)

Everything lives in one **resource group** (a folder for related Azure resources) called
`cachescope-rg`, provisioned by [`infra/main.bicep`](../infra/main.bicep):

| Resource | What it is | Role |
|---|---|---|
| **cachescope-env** | Container Apps **Environment** | the shared network/logging boundary that *hosts* the container apps |
| **cachescope-api** | Container **App** running the API image (from GHCR) | the .NET backend; public HTTPS ingress; custom domain `api.cachescope.dev`; **single replica** |
| **cachescope-redis** | Container **App** running the `redis` image | L3 cache; internal-only |
| **cachescope-sql-…** | Azure SQL **logical server** | hosts databases |
| **CacheScope** (on that server) | Azure SQL **Database** | L4 source of truth; serverless, auto-pause |
| **cachescope-ai-…** | **Application Insights** | telemetry store (traces/metrics/logs) |
| **cachescope-logs-…** | **Log Analytics workspace** | the underlying log store behind App Insights |

Diagram:

```
   Resource group: cachescope-rg
   ┌──────────────────────────────────────────────────────────────┐
   │  cachescope-env  (Container Apps environment)                 │
   │   ├── cachescope-api    (your image; external HTTPS ingress)  │
   │   └── cachescope-redis  (redis image; internal only)          │
   │                                                                │
   │  cachescope-sql-…  (SQL server) ── CacheScope (database, L4)   │
   │  cachescope-ai-…   (App Insights) ── cachescope-logs-… (Log Analytics) │
   └──────────────────────────────────────────────────────────────┘
```

## 10.4 Prerequisites (one-time)

1. **Azure account** with a subscription; `az login` on your machine.
2. **A container image** pushed to a registry (GHCR). The image must exist before the container app
   can pull it.
3. **A domain** delegated to Cloudflare (here `cachescope.dev`).
4. Tools: `az` (Azure CLI), `docker`, `gh`/`git`, `dotnet`.

## 10.5 Step 1 — build & push the image to GHCR

**GHCR** (GitHub Container Registry) is the shelf Azure pulls the image from.

```bash
echo "$GHCR_TOKEN" | docker login ghcr.io -u <github-user> --password-stdin
docker build -t ghcr.io/<user>/cachescope-host:latest ./src
docker push ghcr.io/<user>/cachescope-host:latest
```
Make the package **public** (GitHub → Packages → visibility) so Azure can pull it with no
credentials. (The Bicep also supports a private image via a token.)

The image is built by [`src/Dockerfile`](../src/Dockerfile), a **multi-stage** build: a big .NET SDK
image compiles/publishes the app, then only the published output is copied into a small ASP.NET
runtime image that actually runs.

## 10.6 Step 2 — provision Azure (Bicep)

Register the required resource providers once (Azure needs them enabled), then deploy:

```bash
az provider register -n Microsoft.App
az provider register -n Microsoft.OperationalInsights
az provider register -n Microsoft.Sql

# one command provisions the whole topology (helper wraps the Bicep):
RG=cachescope-rg LOCATION=centralindia SQL_PW='<strong-pw>' \
IMAGE='ghcr.io/<user>/cachescope-host:latest' ./infra/deploy.sh
```

This creates every resource in [10.3](#103-the-azure-resources-what-each-one-is) and prints the API's
`*.azurecontainerapps.io` FQDN. On first boot the app runs **EF migrations** to create the schema and
seed 100 products. `./infra/teardown.sh` deletes everything.

Verify:
```bash
curl -i https://<the-fqdn>/health          # 200
curl -i https://<the-fqdn>/api/products/5   # 200 + X-Served-By
```

## 10.7 Step 3 — Cloudflare: DNS, TLS, edge, frontend

Cloudflare does **five** jobs here. Steps:

**(a) Delegate DNS.** Add `cachescope.dev` to Cloudflare; in the registrar, change the domain's
**nameservers** to the two Cloudflare gave you. Wait until the zone shows **Active**. (Until this,
none of Cloudflare's records are authoritative.)

**(b) Point the API hostname at Azure & issue TLS.**
```bash
# get the app's domain-verification id:
az containerapp show -n cachescope-api -g cachescope-rg --query properties.customDomainVerificationId -o tsv
```
In Cloudflare DNS add:
- `CNAME  api  →  <the *.azurecontainerapps.io FQDN>`  (proxy **OFF/grey** during validation)
- `TXT    asuid.api  →  <the verification id>`

Then bind + issue the managed certificate:
```bash
az containerapp hostname add  -n cachescope-api -g cachescope-rg --hostname api.cachescope.dev
az containerapp hostname bind -n cachescope-api -g cachescope-rg --hostname api.cachescope.dev \
  --environment cachescope-env --validation-method CNAME
```

**(c) Turn on the edge (L0).** Set Cloudflare **SSL/TLS → Full (strict)**, then flip the `api` DNS
record to **Proxied (orange)**. Add a **Cache Rule**: `GET /api/products/*` → *Eligible for cache*
(respect origin TTL). Repeat product reads now return `CF-Cache-Status: HIT`.

**(d) Host the frontend.** Connect the repo to Cloudflare Pages/Workers:
- Build command: `cd web && npm ci && npx ng build`
- Output dir: `web/dist/web/browser`
- Node version is pinned by the repo's `.node-version` (Angular needs a recent Node).
- [`wrangler.jsonc`](../wrangler.jsonc) declares the static-assets deploy (SPA fallback).
- Add `cachescope.dev` + `www` as custom domains on the frontend project.

**(e) Optional — edge analytics + purge.** For the L0 stats card, set on the container app:
```bash
az containerapp secret set -n cachescope-api -g cachescope-rg --secrets cf-analytics-token=<token>
az containerapp update -n cachescope-api -g cachescope-rg \
  --set-env-vars "Cloudflare__ZoneId=<zone-id>" "Cloudflare__ApiToken=secretref:cf-analytics-token"
```
The token (with **Analytics:Read**) lets the edge-stats poller query Cloudflare's GraphQL API. A
Cache-Purge-scoped token additionally powers the "Purge Cloudflare" button. **Secrets go into Azure
Container App secrets, never into the repo.**

## 10.8 Step 4 — continuous delivery (GitHub Actions)

Once infra exists, every push to `main` deploys automatically via
[`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml):

1. **test** job: `dotnet test` — if it fails, nothing ships.
2. **build-and-deploy** job (needs test): build the image → push to GHCR (tagged with the commit sha)
   → `az containerapp update --image …:<sha>` which pulls the new image and rolls out a **new
   revision** (a new version of the running app), shifting traffic to it.

It authenticates to Azure with **OIDC** (a short-lived federated trust — no long-lived secrets
stored). Required repo config: secrets `AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID`; variables
`AZURE_RESOURCE_GROUP`, `CONTAINERAPP_NAME`.

> Azure does **not** store the image — GHCR does. Container Apps *pulls* the referenced image. A
> "deploy" is just telling the container app which image tag to run.

The **frontend** deploys on the same push but via Cloudflare's own Git build (not GitHub Actions).

## 10.9 The complete flow, end to end

```
 developer edits code
        │ git push main
        ├────────────────────────────► GitHub Actions
        │                                 test → build image → push GHCR → az containerapp update
        │                                                                        │
        │                                                                        ▼
        │                                                        Azure Container Apps pulls image,
        │                                                        starts a new revision (API live)
        │
        └────────────────────────────► Cloudflare (Git build)
                                          npm ci → ng build → deploy static assets to edge
                                                                        │
                                                                        ▼
                                                        cachescope.dev serves the new SPA
```

A user then hits `cachescope.dev` (SPA from Cloudflare) which calls `api.cachescope.dev` (Cloudflare
edge → Azure Container App → Redis → SQL), with SignalR over WebSocket and telemetry to App Insights.

## 10.10 Cost-control behaviours (architecture, not billing detail)

The container app **scales to zero** when idle and the SQL database **auto-pauses**, so an idle
deployment consumes almost nothing; the first request after idle triggers a cold start (see
[Chapter 11](11-operations-runbook.md)). `infra/teardown.sh` removes everything when not needed.

---

**Next:** [Chapter 11](11-operations-runbook.md) — how to operate, debug, and safely modify the
system, including the real production issues we hit and fixed.
