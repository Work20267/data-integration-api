# Live demo

A script for presenting this project. Keep it open on a second screen.

Every timing here was measured. Every failure in the playbook has happened.

```
BASE=https://app-di-dev-yqeffct3kgtau.azurewebsites.net
```

---

## Pre-flight — 15 minutes before

### 1 · Warm it up. Not optional.

```bash
curl $BASE/health
curl $BASE/health/ready      # repeat until it returns 200
```

The first call wakes the App Service (~43 s cold on F1, no Always On). The
second resumes Azure SQL serverless, which auto-pauses after 60 minutes idle
(~30–60 s). Warm, both answer in about **0.3 s**.

Skip this and your first slide is a spinner.

### 2 · Start a deployment now, so it finishes while you talk

```bash
gh workflow run cd.yml --ref main
```

**The most important trick here.** A CD run takes 4–5 minutes — far too long to
watch in silence. Start it, demo the app, return to it green. Cooking-show move.

### 3 · Tabs

| | |
|---|---|
| 1 | Terminal, in the repo |
| 2 | `$BASE/openapi/v1.json` |
| 3 | A merged PR with all three checks green |
| 4 | GitHub → Actions, your running deploy |
| 5 | GitHub → Settings → Secrets and variables → Actions |
| 6 | Azure Portal → `rg-di-dev` |

### 4 · Checklist

- [ ] `/health` returns 200 in under a second
- [ ] `/health/ready` returns 200
- [ ] CD run triggered and queued
- [ ] Six tabs open
- [ ] A known-green run bookmarked as fallback — see [Playbook](#when-it-breaks)
- [ ] Decided whether the repo is public (it is **private**; a link you send 404s)

---

## Run of show — about 12 minutes

### 1 · What it is · 1 min

> "It ingests sensor readings from two sources that share nothing structurally,
> normalises them into one canonical model, and the design goal was that a
> mid-batch failure never loses or duplicates a row."

Then **stop**. Their first question tells you what they care about.

### 2 · It is real · 1 min

```bash
curl $BASE/health/ready
```

Healthy, fast, deployed by a pipeline. Not localhost.

### 3 · Ingest · 2 min

```bash
curl -X POST -H "Idempotency-Key: demo-1" $BASE/api/v1/ingest/json-live
```

~15 s, **17,770 rows**. While it runs: rows stream from the HTTP response into
`SqlBulkCopy` chunks, so peak memory is one chunk, not one response — the same
for a 5-minute window as for a year of archive.

Then immediately, same key:

```bash
curl -X POST -H "Idempotency-Key: demo-1" $BASE/api/v1/ingest/json-live
```

**0.06 s**, `"wasAlreadyProcessed": true`. Two commands and idempotency is
demonstrated rather than claimed.

### 4 · The dead letter · 4 min — your best minutes

```bash
curl -X POST $BASE/api/v1/transform/<batchId>
# 17,770 read · 17,707 canonical · 63 dead-lettered · 3.5s
```

> "A non-zero dead-letter count beside a large canonical count is a **success**.
> One unexpected row out of 17,770 must not reject the other 17,769."

```bash
curl $BASE/api/v1/dead-letter/summary
# {"NO_MAPPED_MEASUREMENTS": 63}
```

> "One cause, not sixty-three problems. That's the first thing an operator needs
> to know, so the metric is tagged by error code."

```bash
curl "$BASE/api/v1/dead-letter?take=1"
```

**A Geiger counter on an air-quality network.** Nobody wrote rules for a
radiation sensor. Then fix it live, no code change and no deployment:

```sql
-- sqlcmd defaults QUOTED_IDENTIFIER off; without this the INSERT fails
SET QUOTED_IDENTIFIER ON;
INSERT INTO SourceMapping (SourceKey, SensorModel, SourceField, MetricCode, IsRequired, IsActive)
VALUES ('json-live', 'Radiation Si22G', 'counts_per_minute', 'RADIATION_CPM', 0, 1);
```

```bash
curl -X POST $BASE/api/v1/transform/reload-mappings   # 204
curl -X POST $BASE/api/v1/dead-letter/1/replay        # 200 {"outcome":"replayed"}
```

Show the radiation reading now in the canonical model.

**Do not rush this.** It is the one thing in the demo nobody else will have, and
it shows the architecture doing exactly what it was designed for: absorbing
something nobody anticipated.

---

## 5 · CI/CD · 4 min

Four beats, in order. Do not just show a green tick.

### Beat 1 · The pull request

Open a merged PR. Three checks: **Build & Test**, **Bicep Lint**,
**Infra What-If**.

> "Every change goes through this. Branch protection isn't enforced server-side
> — that needs GitHub Pro on a private repo — so the flow is convention here,
> and the history shows it was followed."

Volunteer that limitation before they find it. It reads as rigour.

### Beat 2 · Infra What-If

Click into the job and show the log. This runs `az deployment group what-if`
**on a pull request**, so a reviewer sees exactly which Azure resources a merge
would create, modify or delete — before merging.

Very few pipelines do this. It is the most differentiated thing in yours.

### Beat 3 · The secrets page — the strongest 30 seconds

**Settings → Secrets and variables → Actions.** Three entries, all GUIDs:

```
AZURE_CLIENT_ID    AZURE_TENANT_ID    AZURE_SUBSCRIPTION_ID
```

> "No publish profile. No connection string. No password. Those are
> identifiers, not credentials.
>
> GitHub gets a short-lived token from Azure through OIDC federation. The App
> Service reaches SQL as a managed identity, so that connection string has no
> credential field at all. And Azure SQL is Entra-only — a SQL admin password
> doesn't exist to be stolen."

Then the war story:

> "The first deploy failed. GitHub now issues **immutable** subject claims with
> the owner and repo IDs embedded — `repo:Work20267@312943019/...` — and every
> tutorial still shows the old `repo:owner/name` format."

### Beat 4 · The run you started

Back to Actions; it should be finishing. Walk the steps:

```
Generate idempotent migration script   →  uploaded as an artifact
Apply migrations                       →  BEFORE the app deploys
Deploy application
Smoke test /health                     →  non-200 fails the deployment
```

> "Schema first, so new code never starts against an old schema. The exact DDL
> that ran is attached to the run as an artifact — reviewable after the fact.
> And the smoke test is a real gate: if `/health` never returns 200, the
> deployment is reported as failed."

Download the `migrations-sql` artifact if there is time.

---

## 6 · Close · 1 min

```bash
dotnet test tests/DataIntegration.IntegrationTests
git log --graph --oneline --all | head -20
```

The chaos test runs against a real SQL Server container: the writer dies after
2 of 5 chunks, 400 committed rows survive, the retry completes, **zero
duplicates**. The graph shows real Gitflow — feature PRs, two releases, a
genuine hotfix.

Mention the eight ADRs exist. Do not read them.

---

## When it breaks

Every failure mode here has an answer that makes you look **better**. That is
not luck — they are all in the [runbook](runbook.md).

| Symptom | What to say | Then |
|---|---|---|
| `/health` slow | "F1 free tier, no Always On — 43 s cold start. It's in the runbook." | Wait |
| `/health/ready` 503 | "Serverless SQL resuming from auto-pause. **This is why liveness deliberately checks nothing** — a probe that queried the database would make App Service recycle a healthy app." | Wait, retry |
| Ingest 502 | "The upstream rate-limited us. The resilience pipeline exhausted its retries and surfaced it as RFC 7807 ProblemDetails, not a 500. Canonical counts are untouched." | Move on |
| Transform returns 0 rows | Already transformed. | Ingest again with a new key |
| Replay returns 409 | "Still failing, with the **current** reason. The row stays pending." | That is correct behaviour |
| CD run fails | Switch to a known-green run | `gh run list --workflow=cd.yml` |
| Everything is down | `docker compose up` | Local, 2 min, identical API |

**Bookmark a known-green CD run before you start.**

---

## Questions you will get

**"Why two data-access technologies?"**
EF Core for config and migrations where the schema changes; `SqlBulkCopy` and
Dapper for the hot path, because EF round-trips per entity and cannot express a
bulk `MERGE`. The cost is two idioms in one codebase — documented in ADR 002.

**"Why no message queue?"**
The staging table *is* the queue, and unlike an in-memory `Channel<T>` it
survives a restart, can be inspected with a `SELECT`, and is shared across
instances. A continuous stream rather than a polled window would change the
answer. ADR 007.

**"What happens if it dies mid-batch?"**
Committed chunks stay, rows are marked as they resolve so a retry skips them,
and the canonical write is a `MERGE` — so the retry cannot duplicate. Proven
against a real SQL Server: 1,000 rows, failure after 2 of 5 chunks, final count
1,000 with zero duplicates.

**"Is Gitflow not outdated?"**
Trunk-based with feature flags is usually better for continuous delivery. This
uses Gitflow because the release model is versioned and I wanted to exercise the
release and hotfix paths. Knowing when each applies matters more than defending
either.

**"What would you do next?"**
Plausibility bounds per metric — the canonical model currently accepts −149 °C
and 1664% humidity. Then a retention policy, and a distributed lock before
running more than one instance.

---

## Do not

- Walk the code file by file. They will ask if they want it.
- Demo only the happy path. The dead letter **is** the project.
- Say "scalable" or "production-ready" without a mechanism attached.
- Quote a number you have not measured.
- Hide the gaps. Naming them first is worth more than hoping nobody asks.

---

## Worth fixing before a real interview

**Switch to B1.** One line in `infra/dev.bicepparam`:

```bicep
param appServicePlanSku = 'B1'
```

Roughly $13/month, covered by the student credit, and the 43-second cold start
disappears along with the 60 CPU-minutes/day cap. For a demo you get one shot
at, that is cheap insurance.

**Decide on repo visibility.** It is private. On your own screen that is fine;
a link you send returns 404. Making it public also exposes the Entra object ID
and UPN in `dev.bicepparam`.
