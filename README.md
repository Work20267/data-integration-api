# Data Integration API

A RESTful data integration service that ingests, transforms, and synchronizes
structured data from mismatched external sources into a canonical model backed by
Azure SQL — with idempotent upserts, per-row error isolation, and dead-letter replay
so a partial failure never means data loss.

> **Status: Phase 1 (foundation).** The service deploys and reports health; the
> integration engine lands in Phases 3–5. See [Roadmap](#roadmap).

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

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Docker.

```bash
# SQL Server for local development (Phase 2 onward)
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Local_Dev_P@ss1" \
  -p 1433:1433 --name sqldev -d mcr.microsoft.com/mssql/server:2022-latest

dotnet restore
dotnet build
dotnet test
dotnet run --project src/DataIntegration.Api
```

The API then serves `/health`, and OpenAPI at `/openapi/v1.json` in Development.

Local secrets go in User Secrets, never in `appsettings.json`:

```bash
dotnet user-secrets --project src/DataIntegration.Api \
  set "ConnectionStrings:Default" "Server=localhost,1433;..."
```

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
