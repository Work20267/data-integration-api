# Data Integration API

A RESTful data integration service that ingests, transforms, and synchronizes
structured data from mismatched external sources into a canonical model backed by
Azure SQL — with idempotent upserts, per-row error isolation, and dead-letter replay
so a partial failure never means data loss.

> **Status: Phase 6 (chaos-tested).** Deployed on Azure, both sources landing in
> one canonical model, and the two guarantees the project rests on are proven
> against a **real SQL Server** rather than a mock: processing the same
> observation twice produces one row, and a failure mid-batch loses nothing.
> Observability and the runbook are Phase 7. See [Roadmap](#roadmap).

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

## API

| Endpoint | Behaviour |
|---|---|
| `POST /api/v1/ingest/{source}` | Pulls the current window from `source` and stages every row. `202` when work was done, `200` when the `Idempotency-Key` had already been processed, `404` for an unknown source, `502` when upstream stays unreachable after retries. |

```bash
curl -X POST -H "Idempotency-Key: demo-1" http://localhost:8080/api/v1/ingest/json-live
# {"batchId":"01a11510-...","sourceKey":"json-live","rowsStaged":17770,
#  "wasAlreadyProcessed":false,"durationSeconds":15.51}

# same key again - recognised, nothing re-ingested
curl -X POST -H "Idempotency-Key: demo-1" http://localhost:8080/api/v1/ingest/json-live
# {"...","rowsStaged":17770,"wasAlreadyProcessed":true,"durationSeconds":0}   HTTP 200 in 0.057s
```

Measured locally against SQL Server in Docker: **17,770 rows in 15.51 s**,
including the 8.5 MB upstream download. Rows stream from the HTTP response
straight into `SqlBulkCopy` chunks of 2,000, so memory stays flat regardless of
how large the window is.

Ingestion deliberately does not interpret the payload — each row is stored
verbatim. That separation is what makes a mapping fix reprocessable without
re-fetching from a volunteer-funded upstream.

### Transformation

| Endpoint | Behaviour |
|---|---|
| `POST /api/v1/transform/{batchId}` | Transforms a batch's pending rows. Safe to repeat — rows are marked as they resolve, and the canonical write is an upsert. |
| `POST /api/v1/transform/reload-mappings` | Drops the rule cache so a newly inserted rule takes effect without a restart. |
| `GET /api/v1/dead-letter` | Lists rows that could not be transformed. |
| `GET /api/v1/dead-letter/summary` | Counts grouped by error code. |
| `POST /api/v1/dead-letter/{id}/replay` | Re-transforms one row with the current rules. `200` if now canonical, `409` with the current reason if it still fails. |

```bash
curl -X POST http://localhost:8080/api/v1/transform/01a11510-...
# {"rowsRead":17770,"rowsCanonical":17707,"rowsDeadLettered":63,"durationSeconds":3.49}
```

**A non-zero dead-letter count alongside a large canonical count is a success,
not a failure.** One unexpected row out of 17,770 must not reject the other
17,769 — see [ADR 005](docs/adr/005-per-row-isolation-and-dead-letter-replay.md).

All 63 failures in that run had a single cause, and it was not corrupt data:

```
GET /api/v1/dead-letter/summary
{"NO_MAPPED_MEASUREMENTS": 63}

"No field on this Radiation Si22G reading resolved to a known metric."
```

Someone has a **Geiger counter** on an air-quality network. The fix needs no
code change and no deployment:

```sql
INSERT INTO SourceMapping (SourceKey, SensorModel, SourceField, MetricCode, IsRequired, IsActive)
VALUES ('json-live', 'Radiation Si22G', 'counts_per_minute', 'RADIATION_CPM', 0, 1);
```
```bash
curl -X POST .../api/v1/transform/reload-mappings   # 204
curl -X POST .../api/v1/dead-letter/1/replay        # 200 {"outcome":"replayed"}
```

### Idempotency

The guarantee the project rests on: **processing the same observation twice
produces one row.** Measured, not asserted:

```
re-process the identical 4,560 archive rows
  rowsCanonical reported        4,560     the writer did the work
  readings      22,163 → 22,163           UNCHANGED
  measurements  58,975 → 58,975           UNCHANGED
  duplicate ExternalKeys            0
```

Three things make it work, and all three are easy to get wrong:

1. **`ExternalKey` is truncated to whole seconds.** The live feed writes
   `2026-10-04 00:00:15`, the archive writes `2026-10-04T00:00:15`. A key that
   kept milliseconds would store the same reading twice and fail *silently*.
2. **The write is a `MERGE` with `HOLDLOCK`**, against the `ExternalKey` unique
   index — without the lock hint, `MERGE` can race itself and violate that index.
3. **Nulls mean "this source does not say."** The archive has no country or
   indoor flag, so it must not overwrite what the live feed supplied.

See [ADR 006](docs/adr/006-two-sources-one-canonical-model.md), which also
records how the proof was constructed and why the cross-source version of it
cannot be run against the live upstream on demand.

### Chaos tests

Those two claims are properties of **SQL Server** — of `MERGE`, `HOLDLOCK`, a
unique index, and what a transaction commits before a failure. A faked writer
would assert them against my own assumptions about those mechanisms, which is
the thing in doubt. So the critical tests run against a real SQL Server in a
Testcontainers instance, migrated with the same migrations CD applies to Azure:

| Test | Asserts |
|---|---|
| Same observations twice | 500 rows, then identical payloads in a **new batch** → counts unchanged, zero duplicate keys |
| **Failure mid-batch** | 1,000 rows, writer throws after 2 of 5 chunks → 400 committed survive, exactly 600 stay `Pending`, retry processes exactly those 600, final 1,000 rows, **zero duplicates** |
| Both sources converge | 100 live observations, then the same 100 in CSV → **100 rows, not 200** |

The failure is deterministic rather than a container kill. The fixture *can*
stop the container, but that makes the test depend on the kill landing between
two specific chunks — and a flaky test is worse than no test. The invariant is
the same either way.

**Writing the third test found a real bug.** The CSV parser emits
`Country = null` meaning "this source does not say", but the `MERGE` did
`target.Country = source.Country` — so ingesting the archive after the live feed
would have silently erased the country on every overlapping reading. No
exception, no dead letter, no count discrepancy; it would have looked like clean
data. Fixed with `COALESCE`, and now asserted directly. See
[ADR 008](docs/adr/008-proving-zero-loss-against-real-sql-server.md).

### Scheduled sync

`SyncWorker` runs ingest → transform → advance-watermark per source on a timer.
`GET /api/v1/sync/status` reports whether it is enabled and how far each source
has got.

**It is off by default**, which is a decision rather than an oversight: the App
Service free tier allows 60 CPU-minutes per day, and a loop polling an 8.5 MB
feed would exhaust it until the site began returning 403 — besides being rude to
a volunteer-funded upstream.

```jsonc
// appsettings.json
"Sync": { "Enabled": true, "IntervalMinutes": 10, "Sources": [ "json-live" ] }
```

**There is no in-memory queue between ingest and transform, deliberately.** The
staging table already *is* the queue, and unlike a `Channel<T>` it survives a
restart, can be inspected with a `SELECT`, and is shared across instances.
Backpressure — the usual reason to reach for a channel — is already provided by
the chunk size. See [ADR 007](docs/adr/007-the-staging-table-is-the-queue.md).

A cycle that fails returns a result rather than throwing. An exception escaping
a `BackgroundService` stops it silently or brings the host down, so **one bad
cycle would end all future syncing**; instead the worker logs it and the next
tick retries.

### Adding a source

Supporting the CSV archive required a parser, a reader, and two DI lines. The
mapping engine, canonical writer, dead-letter store and transform service were
**not modified** — and the archive's nine mapping rules had been seeded in
Phase 2, before the parser existed. First run: 4,560 rows, zero dead letters.

### Known gaps

**No plausibility checking.** Canonical data accepts physically impossible
values — temperatures from −149 °C to 614 °C, humidity above 1600%, negative
particulate counts. The pipeline ingests faithfully, which is correct, but a
broken sensor's output is stored as though it were an observation. Min/max
bounds per metric on `SourceMapping` plus a `VALUE_OUT_OF_RANGE` code would
route these through the dead-letter machinery that already exists. See ADR 005.

**A truncated archive listing looks like a small day.** One listing fetch
returned 280 KB instead of 4.5 MB and contained only `bme280` entries — which is
why all 4,560 archive readings are BME280 despite the filter also allowing
`sds011`. The reader cannot currently tell a short day from a short response.
See ADR 006.

**No cross-instance coordination.** Two instances with sync enabled would both
run cycles. The per-minute idempotency key makes the collision harmless rather
than duplicating data, but that is luck rather than design — a real
multi-instance deployment wants a distributed lock. See ADR 007.

**No retention policy.** Staging rows and dead letters accumulate indefinitely.

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

Gitflow. `main` is production and `develop` is the integration branch. Changes
reach them through pull requests with a green CI run.

> Server-side branch protection is **not** enabled: it requires GitHub Pro on a
> private repository. The flow is followed by convention and visible in the
> history (feature PR → `develop`, release branch → `main` plus a back-merge),
> but nothing currently prevents a direct push. Making the repository public, or
> upgrading the plan, would enforce it.

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
