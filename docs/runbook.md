# Runbook

Operational procedures for the Data Integration API.

Every failure mode below was encountered while building the service, not
imagined. Where a command is given, it is one that was actually run.

---

## Before a demo

Two cold starts will make a working system look broken.

```bash
# Wakes the App Service and resumes the serverless database.
curl -s https://<app>.azurewebsites.net/health
curl -s https://<app>.azurewebsites.net/health/ready
```

| Expect | First call | Warm |
|---|---|---|
| `/health` | up to ~43 s on F1 | ~0.3 s |
| `/health/ready` | 503 then 200, up to ~60 s | ~0.3 s |

Do this **five minutes before**, not during. On F1 there is no Always On, and
Azure SQL serverless auto-pauses after 60 minutes idle.

---

## Alerts

Both are provisioned by [`infra/modules/alerts.bicep`](../infra/modules/alerts.bicep),
so an environment cannot exist without them.

| Alert | Fires when | Severity |
|---|---|---|
| `alert-<env>-5xx` | More than 5 server errors in 15 minutes | 2 |
| `alert-<env>-deadletters` | More than 500 dead letters in an hour | 3 |

---

## 1 · Dead letters have spiked

**Alert:** `alert-<env>-deadletters`

### Diagnose

Always start here. It takes one request and answers the only question that
matters first — is this *one* problem or *several*?

```bash
curl -s https://<app>.azurewebsites.net/api/v1/dead-letter/summary
```

```jsonc
{"NO_MAPPED_MEASUREMENTS": 63}        // one cause  → configuration gap
{"INVALID_NUMERIC_VALUE": 2140,       // several    → the upstream changed shape
 "MISSING_TIMESTAMP": 1890}
```

Then read one example:

```bash
curl -s "https://<app>.azurewebsites.net/api/v1/dead-letter?take=1"
```

### `NO_MAPPED_MEASUREMENTS` — a sensor model with no rules

**Not corrupt data.** Someone added a device nobody wrote rules for. This has
already happened once in production: a Geiger counter (`Radiation Si22G`)
appeared on an air-quality network and dead-lettered 63 rows.

Fix it with configuration, no deployment:

```sql
-- sqlcmd defaults QUOTED_IDENTIFIER OFF, which SQL Server rejects on a table
-- with a unique index. Without this line the INSERT fails.
SET QUOTED_IDENTIFIER ON;

INSERT INTO SourceMapping (SourceKey, SensorModel, SourceField, MetricCode, IsRequired, IsActive)
VALUES ('json-live', '<model>', '<field>', '<METRIC_CODE>', 0, 1);
```

```bash
curl -X POST .../api/v1/transform/reload-mappings     # 204 — rules are cached
curl -X POST .../api/v1/dead-letter/<id>/replay       # 200 {"outcome":"replayed"}
```

A replay that returns **409** means the row still fails, with the *current*
reason attached. The row stays pending; fix the actual cause and replay again.

Afterwards, add the rule to
[`SourceMappingConfiguration`](../src/DataIntegration.Infrastructure/Persistence/Configurations/SourceMappingConfiguration.cs)
and ship a migration, or the next environment will hit the same gap.

### `INVALID_NUMERIC_VALUE` or `MISSING_TIMESTAMP` in bulk — upstream changed

Thousands of rows failing at once is a schema change upstream, not bad sensors.
The raw payload is preserved, so **nothing is lost**: fix the parser, deploy,
and replay. Do not purge the dead letters.

---

## 2 · The API returns 5xx

**Alert:** `alert-<env>-5xx`

```bash
curl -s -o /dev/null -w "%{http_code} in %{time_total}s\n" https://<app>.azurewebsites.net/health
curl -s https://<app>.azurewebsites.net/health/ready
```

| Observation | Cause | Action |
|---|---|---|
| `/health` 200, `/health/ready` 503 | Azure SQL resuming from auto-pause | Wait ~60 s. **Not an incident.** |
| `/health` 403 | F1 daily CPU quota (60 min) exhausted | Resets at midnight UTC, or move to B1 |
| `/health` times out | Cold start | Retry once; see *Before a demo* |
| `/health` 200, endpoints 500 | Application fault | Application Insights → Failures |

`/health` deliberately checks no dependencies — see ADR 003. A liveness probe
that queried the database would recycle the app every time SQL paused.

---

## 3 · Ingest returns 502 Upstream unavailable

```jsonc
{"title":"Upstream unavailable",
 "detail":"Source 'csv-archive' could not be reached after retries."}
```

The resilience pipeline exhausted retry, timeout and circuit breaker. The
upstream is down or has rate-limited us — the archive does this under light
load.

**Nothing is corrupted.** Canonical counts are untouched and a partial batch is
retained for reprocessing. Wait and retry.

If it recurs, **reduce load before retrying**:

```jsonc
"SensorCommunity": {
  "ArchiveMaxFiles": 10,        // down from 25
  "ArchiveFetchDelayMs": 1000   // up from 300
}
```

The upstream is volunteer-funded. A sustained backfill warrants contacting the
operators rather than tuning around them.

---

## 4 · Deployment failed

### At `Azure login (OIDC)`

```
AADSTS700213: No matching federated identity record found for presented
assertion subject 'repo:Owner@123/repo@456:environment:dev'
```

GitHub issues **immutable subject claims** — owner and repo IDs embedded inline,
so renaming cannot silently transfer access. Nearly every tutorial still shows
the classic `repo:owner/name:ref` form.

Compare what was presented against what exists:

```bash
az ad app federated-credential list --id <AZURE_CLIENT_ID> \
  --query "[].{name:name, subject:subject}" -o table
```

Add a credential whose subject matches the error **exactly**.

### At `Apply migrations`

The GitHub Actions identity needs a database user. This is T-SQL and cannot be
expressed in Bicep:

```sql
CREATE USER [github-deploy-data-integration] FROM EXTERNAL PROVIDER;
ALTER ROLE db_ddladmin ADD MEMBER [github-deploy-data-integration];
ALTER ROLE db_datareader ADD MEMBER [github-deploy-data-integration];
ALTER ROLE db_datawriter ADD MEMBER [github-deploy-data-integration];
```

Run as the Entra administrator. The exact DDL that was attempted is attached to
the failed run as the `migrations-sql` artifact.

### At `Smoke test /health`

Infrastructure and migrations succeeded; the app did not start. Check
Application Insights, then the App Service log stream. The previous deployment
is still live — App Service does not swap in a site that fails to start.

### `RequestDisallowedByAzure` on `Deploy infrastructure`

Azure for Students restricts deployments to five regions. Application Insights
is **not available in `indiasouthcentral`**, which is why this project runs in
`uaenorth`.

```bash
az policy assignment show --name sys.regionrestriction --query parameters
```

---

## 5 · Duplicate canonical rows

This should be impossible. If it happens, the idempotency guarantee has broken.

```sql
SELECT ExternalKey, COUNT(*) FROM CanonicalReading
GROUP BY ExternalKey HAVING COUNT(*) > 1;
```

Likely causes, in order:

1. The unique index on `ExternalKey` was dropped by a migration
2. `HOLDLOCK` was removed from the `MERGE` — it can then race itself
3. `BuildExternalKey` stopped truncating to whole seconds, so the live feed and
   the archive produce different keys for the same observation

All three are covered by tests
([ADR 008](adr/008-proving-zero-loss-against-real-sql-server.md)). A duplicate
in production means a test was deleted or skipped — check that first.

---

## 6 · Scheduled sync is not running

```bash
curl -s https://<app>.azurewebsites.net/api/v1/sync/status
```

```jsonc
{"enabled": false, ...}
```

**Off by default, deliberately** — a polling loop would exhaust the F1 daily CPU
allowance and is impolite to the upstream. Enable with `Sync:Enabled`.

If `enabled` is true but `watermarks` are stale, check the logs for event
`1304` (`SyncCycleFailed`). A failed cycle is logged and the worker continues;
it does not stop.

---

## Useful queries

```sql
-- Pipeline state at a glance
SELECT LastSourceKey, COUNT(*) FROM CanonicalReading GROUP BY LastSourceKey;
SELECT Status, COUNT(*) FROM StagingRecord GROUP BY Status;   -- 0 Pending, 1 Transformed, 2 Rejected
SELECT Status, COUNT(*) FROM DeadLetterRecord GROUP BY Status;

-- Work stuck pending
SELECT b.Id, b.SourceKey, b.Status, COUNT(s.Id) AS pending
FROM IngestBatch b JOIN StagingRecord s ON s.IngestBatchId = b.Id
WHERE s.Status = 0 GROUP BY b.Id, b.SourceKey, b.Status;
```

Re-running a batch is always safe: rows are marked as they resolve, and the
canonical write is an upsert.

```bash
curl -X POST .../api/v1/transform/<batchId>
```

---

## Known gaps

| Gap | Impact |
|---|---|
| No plausibility checking | −149 °C and 1664% humidity are stored as observations (ADR 005) |
| No retention policy | Staging rows and dead letters accumulate indefinitely |
| No distributed lock | Two instances would both run sync; harmless but unplanned (ADR 007) |
| Truncated archive listing | Looks like a small day rather than an error (ADR 006) |
