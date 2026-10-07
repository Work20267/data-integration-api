# Data Integration API

A RESTful data integration service that ingests, transforms, and synchronizes
structured data from mismatched external sources into a canonical model backed by
Azure SQL — with idempotent upserts, per-row error isolation, and dead-letter replay
so a partial failure never means data loss.

> **Status: Phase 2 (data layer).** Infrastructure is deployed on Azure and the
> canonical schema is live in Azure SQL. The ingestion, transformation and sync
> engine lands in Phases 3–5. See [Roadmap](#roadmap).

## Data sources

Two feeds from [Sensor.Community](https://sensor.community), a volunteer-run
network of DIY air-quality sensors (ESP8266 boards with SDS011 dust sensors and
BME280 climate sensors). Data is ODbL / CC-BY-SA.

| | Source 1 | Source 2 |
|---|---|---|
| Endpoint | `data.sensor.community/static/v2/data.json` | `archive.sensor.community/<date>/` |
| Shape | Nested JSON; measurements are a `sensordatavalues` array of `{value_type, value}` pairs | Flat, semicolon-delimited CSV with **different columns per sensor model** |
| Volume | ~17,700 readings per 5-minute window | ~17,800 files/day, several hundred rows each |
| Auth | None | None |

They were chosen for three properties:

1. **Structurally different, not cosmetically.** A key-value array against flat
   per-model columns means the transformation layer has real work to do.
2. **The field names already disagree.** The same measurement is
   `pressure_at_sealevel` in the JSON and `pressure_sealevel` in the CSV. That is
   why mapping lives in the `SourceMapping` table rather than in code.
3. **They overlap on purpose.** The archive is where the live feed lands a day
   later, so re-ingesting a day already streamed live must produce *zero* new
   rows. Idempotency is a requirement the data imposes, not a contrived test.

One upstream trap worth knowing: in this schema `P1` is **PM10** and `P2` is
**PM2.5** — `P1` is not PM1, which is `P0`. Mapping by name rather than by
meaning is silently wrong. See [ADR 001](docs/adr/001-data-sources.md).

The archive is volunteer-funded: fetch politely, cache locally, and throttle.

## Architecture

```
external source ──▶ /api/v1/ingest ──▶ staging ──▶ transform ──▶ canonical ──▶ sync
                                          │            │
                                          │            └──▶ dead letter ──▶ replay
                                          └──▶ raw payload preserved
```

| Concern | Choice | Why |
|---|---|---|
| Runtime | .NET 10 (LTS), ASP.NET Core | Supported into 2028 |
| Config / metadata | EF Core 10 + migrations | Rich modelling, low volume |
| Bulk staging load | `SqlBulkCopy` | Orders of magnitude faster than `SaveChanges` |
| Upserts / hot reads | Dapper + `MERGE` | EF cannot express an efficient bulk upsert |
| Resilience | `Microsoft.Extensions.Http.Resilience` (Polly v8) | Retry, circuit breaker, timeout |
| Backpressure | Bounded `System.Threading.Channels` | Bounded queue instead of unbounded memory growth |
| Telemetry | OpenTelemetry → Azure Monitor | Vendor-neutral, not Azure-locked |
| Tests | xUnit v3 + Testcontainers | Integration tests run against real SQL Server |
| Infrastructure | Bicep | Idempotent, reviewable via `what-if` |
| CI/CD | GitHub Actions + OIDC | No long-lived Azure secrets |

Design decisions are recorded in [`docs/adr/`](docs/adr/).

## Running locally

Needs only **Git and Docker** — no .NET SDK required:

```bash
git clone https://github.com/Work20267/data-integration-api
cd data-integration-api
docker compose up
```

That brings up SQL Server and the API together; the API waits on SQL's health
check, so it never starts against a database that is not yet accepting queries.
It then serves `/health` on port 8080, with OpenAPI at `/openapi/v1.json`.

To work on the code you also need the [.NET 10 SDK](https://dotnet.microsoft.com/download)
(the exact version is pinned in `global.json`):

```bash
docker compose up sql -d          # database only
dotnet restore
dotnet test
dotnet ef database update --project src/DataIntegration.Infrastructure \
                          --startup-project src/DataIntegration.Api
dotnet run --project src/DataIntegration.Api
```

The local SA password in `compose.yaml` and `appsettings.Development.json` is a
throwaway container credential, not a secret. No production credential exists in
this repository at all — see [Infrastructure](#infrastructure).

### Health endpoints

| Endpoint | Checks | Used by |
|---|---|---|
| `/health` | Nothing — liveness only | CD smoke test, App Service probe |
| `/health/ready` | SQL connectivity | Readiness, reported but never gated on |

Liveness deliberately touches no dependency: Azure SQL serverless auto-pauses
after 60 minutes idle, and a probe that queried the database would report
unhealthy during a resume — causing App Service to recycle a healthy app.

## Project layout

```
src/
  DataIntegration.Api/             controllers, middleware, DI wiring
  DataIntegration.Core/            entities, interfaces, transformation rules
  DataIntegration.Infrastructure/  EF Core, connectors, repositories
tests/
  DataIntegration.UnitTests/       transformation rules, validators
  DataIntegration.IntegrationTests/ WebApplicationFactory + Testcontainers
infra/
  main.bicep                       resource group composition
  modules/                         monitoring, sql, appservice
  dev.bicepparam                   per-environment parameters
.github/workflows/                 ci.yml, cd.yml
```

## Infrastructure

Provisioned entirely by Bicep — App Service (Linux), Azure SQL on the free
serverless offer, Log Analytics + Application Insights, and a user-assigned
managed identity.

The App Service authenticates to Azure SQL as that managed identity
(`Authentication=Active Directory Managed Identity`), and GitHub Actions
authenticates to Azure via OIDC federated credentials. **There is no password or
publish profile stored anywhere in this repository or in GitHub Secrets.**

```bash
az deployment group what-if --resource-group rg-di-dev \
  --template-file infra/main.bicep --parameters infra/dev.bicepparam

az deployment group create --resource-group rg-di-dev \
  --template-file infra/main.bicep --parameters infra/dev.bicepparam
```

## Branching

Gitflow. `main` is production and `develop` is the integration branch; both are
protected and require a green CI run.

| Branch | Cut from | Merges to |
|---|---|---|
| `feature/*` | `develop` | `develop` |
| `release/*` | `develop` | `main` **and** `develop` |
| `hotfix/*` | `main` | `main` **and** `develop` |

## Roadmap

| Phase | Scope | Gate |
|---|---|---|
| 1 | Foundation, IaC, CI/CD | Live Azure URL, no secrets |
| 2 | Schema, EF Core, managed-identity SQL | Passwordless SQL access |
| 3 | Source #1 connector, staging | Rows staged, survives upstream failure |
| 4 | Transformation engine, dead letter | Bad rows isolate, replay works |
| 5 | Sync, idempotency, source #2 | Double-ingest is a no-op |
| 6 | Chaos test | Zero loss, zero duplicates mid-failure |
| 7 | Observability, runbook, docs | Alert fires, runbook recovers |
