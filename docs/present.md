# Presenting · command sheet

A standalone live demo of the deployed system, weighted toward cloud concepts.
No slides. Keep this open on a second screen.

Where to type is marked throughout:

| | |
|---|---|
| **T** | Terminal |
| **B** | Browser |
| **Q** | Azure Portal → `sqldb-di-dev` → Query editor |

Every timing below was measured against the live instance, not estimated.

---

## Measured behaviour

| | |
|---|---|
| `/health` cold | **43.7 s** — App Service waking (F1, no Always On) |
| `/health/ready` first call after that | **503** at ~30 s — Azure SQL still resuming |
| `/health/ready` second call | **200 Healthy in 0.52 s** |
| Both, warm | ~0.3–0.5 s |
| Ingest, warm | ~6 s for ~17,700 rows |
| Transform, warm | ~9 s |

**Readiness needs two calls after a cold start.** Budget for it.

> `/openapi/v1.json` returns **404** on Azure. `MapOpenApi()` is inside
> `if (app.Environment.IsDevelopment())` in `Program.cs`, and App Service runs
> Production. It works locally on `:8080`; do not open it against Azure.

---

## Prep · T-20 minutes

**T**

```bash
brew install jq                 # one-off; makes JSON readable on a projector
cd ~/data-integration-api
export BASE=https://app-di-dev-yqeffct3kgtau.azurewebsites.net
```

```bash
curl -s $BASE/health                    # ~44 s cold → Healthy
curl -s $BASE/health/ready              # may be 503 the first time
curl -s $BASE/health/ready              # → Healthy
```

Repeat the last one until it returns `Healthy`. **This step is not optional** —
skip it and your opening is a spinner.

```bash
gh workflow run cd.yml --ref main       # your finale; takes 4-5 minutes
```

Start the deployment now and return to it green in Beat 6. A CD run is far too
long to watch in silence.

**B** — five tabs, left to right:

| Tab | |
|---|---|
| 1 | `$BASE/health/ready` |
| 2 | `portal.azure.com` → resource group `rg-di-dev` |
| 3 | `portal.azure.com` → `sqldb-di-dev` → **Query editor**, already signed in |
| 4 | GitHub → **Actions** |
| 5 | GitHub → **Settings → Secrets and variables → Actions** |

Checklist:

- [ ] `/health/ready` returns `Healthy` in under a second
- [ ] CD run triggered
- [ ] Query editor signed in — the Entra sign-in sometimes wants extra MFA
- [ ] Display sleep set to Never
- [ ] Screenshots of every beat in a folder, in order, as insurance

---

## Cold open · 30 s · no screen

> "Public environmental sensor networks publish the same readings through two
> incompatible feeds — a live JSON API and a daily CSV archive that disagree on
> field names, timestamp format and structure. The devices are hobbyist-built,
> so readings arrive late, twice, or malformed. We built a cloud-deployed API
> that normalises both into one canonical model, and the design goal was that a
> mid-batch failure loses nothing and duplicates nothing."

Then **stop**. A question here tells you what they care about.

---

## Beat 1 · It is deployed, and provisioned by code · 75 s

**B tab 1** — reload. `Healthy`. Point at the address bar: `azurewebsites.net`,
not localhost.

**B tab 2** — `rg-di-dev`. Count aloud: **12 resources**.

> **Infrastructure as Code.** "All twelve came from one Bicep template. Nobody
> clicked through the portal. The deployment is declarative — we describe desired
> state and Azure reconciles — so re-running it is a no-op."

Then, unprompted:

> "It is in UAE North, not India. Azure for Students limits us to five regions,
> and Application Insights is not offered in India South Central — so the region
> was chosen by **per-region service availability**, not by latency."

And name the model:

> "This is PaaS end to end. We provision no virtual machines and patch no
> operating system."

---

## Beat 2 · Ingest live data · 90 s

**T**

```bash
curl -s -X POST -H "Idempotency-Key: demo-1" \
  $BASE/api/v1/ingest/json-live | tee /tmp/ingest.json | jq
```

```json
{ "batchId": "…", "sourceKey": "json-live", "rowsStaged": 17743,
  "wasAlreadyProcessed": false, "durationSeconds": 5.78 }
```

**Capture the id immediately — this is the one place a live demo breaks:**

```bash
BATCH=$(jq -r .batchId /tmp/ingest.json); echo $BATCH
```

Without `jq`:

```bash
BATCH=$(grep -o '"batchId":"[^"]*"' /tmp/ingest.json | cut -d'"' -f4); echo $BATCH
```

While the ingest runs:

> "Rows stream from the HTTP response straight into bulk-copy chunks, so peak
> memory is one chunk — not one response. The same code handles a five-minute
> window or a year of archive."

Now the **identical** command again:

```bash
curl -s -X POST -H "Idempotency-Key: demo-1" $BASE/api/v1/ingest/json-live | jq
```

**0.06 s**, `"wasAlreadyProcessed": true`.

> "Same key, so nothing was re-fetched. The status code differs too — 202 when
> work happened, 200 for a recognised replay. Idempotency demonstrated in two
> commands rather than claimed on a slide."

---

## Beat 3 · Transform · 60 s

**T**

```bash
curl -s -X POST $BASE/api/v1/transform/$BATCH | jq
```

```json
{ "rowsRead": 17743, "rowsCanonical": 17729,
  "rowsDeadLettered": 14, "durationSeconds": 8.94 }
```

> "A non-zero dead-letter count beside a large canonical count is the **success**
> case. Per-row isolation means one unmappable row must not reject the other
> 17,729."

---

## Beat 4 · The data, in a managed database · 90 s

**Q**

```sql
SELECT TOP 10 SensorId, SensorModel, Country, ObservedAtUtc, ExternalKey
FROM CanonicalReading ORDER BY Id DESC;

SELECT MetricCode, COUNT(*) AS readings, MIN(Value) AS lo, MAX(Value) AS hi
FROM CanonicalMeasurement GROUP BY MetricCode ORDER BY 2 DESC;
```

Ten metrics, real countries, today's timestamps.

> **Serverless, consumption-billed compute.** "This database auto-pauses after
> 60 minutes idle — compute scales to zero and we pay for storage alone. That is
> also why my first call this morning took 44 seconds: a resume, not a fault."

---

## Beat 5 · The Geiger counter · 2 min · your best two minutes

**T**

```bash
curl -s $BASE/api/v1/dead-letter/summary | jq
```

```json
{ "NO_MAPPED_MEASUREMENTS": 14 }
```

> "One cause, not fourteen problems. That is the first thing an operator needs
> to know, which is why the metric is tagged by error code."

```bash
curl -s "$BASE/api/v1/dead-letter?take=1" | jq ".[0] | {id, errorCode, rawRowPreview}"
```

**Q**

```sql
SELECT COUNT(*) AS radiation_readings
FROM CanonicalMeasurement WHERE MetricCode = 'RADIATION_CPM';
```

**50.**

> "A Geiger counter on an air-quality network. Nobody wrote rules for a radiation
> sensor, so 63 rows failed the first time. We added one mapping rule — a
> database row, no code change, no redeploy — and replayed them from staged
> storage rather than re-fetching upstream. That rule now ships in a migration,
> which is why only 14 fail today."

**Do not rush this.** It is the one thing nobody else will have, and it shows the
architecture absorbing something nobody anticipated.

---

## Beat 6 · The pipeline, and where the credentials are not · 2 min

**B tab 4** — the run you triggered, green by now. Walk the step order:

```
Generate migration script → Apply migrations → Deploy application → Smoke test /health
```

> **Release orchestration.** "Schema first, deliberately — new code must never
> start against an old schema. The exact DDL that ran is attached to the run as
> an artifact, reviewable after the fact. And the smoke test is a real gate: if
> `/health` never returns 200, the deployment is reported as failed."

**B tab 5** — three entries, all GUIDs.

> **Workload identity federation.** "No publish profile. No connection string.
> No password. Those are identifiers, not credentials. GitHub obtains a
> short-lived token from Azure through OIDC. The App Service reaches SQL as a
> managed identity, so that connection string has no credential field at all.
> And the SQL server is Entra-only — **an admin password does not exist to be
> stolen.**"

Optional, if time allows:

```bash
gh pr create --base develop --head chore/app-service-b1 --fill
```

**B tab 4** → the **Infra What-If** job. One SKU value changed, cascading into
Always On and an App Service health probe, because the template encodes that
both need Basic tier. Concept: *change preview for infrastructure, before merge.*

Do not merge it — B1 is about $13 a month, and the demo value is entirely in the
what-if output, which costs nothing.

---

## Close · 45 s

> "Zero rupees a month, zero stored secrets, and twelve resources that can be
> destroyed and recreated from one template.
>
> What it does not do yet: there are no plausibility bounds, so −149 °C is
> sitting in that table. No retention policy. And no distributed lock, so scaling
> past one instance would double-process a batch. Those are the next three
> things."

Naming the gaps yourself is worth more than hoping nobody asks.

---

## Rescue commands

| If | **T** |
|---|---|
| Transform returned 0 rows | Ingest again with a new key, then re-read `$BATCH` from `/tmp/i2.json` |
| You lost `$BATCH` | `curl -s "$BASE/api/v1/dead-letter?take=1" \| jq -r ".[0].ingestBatchId"` |
| Readiness went 503 again | `curl -s $BASE/health/ready` — twice |
| Need a green CD run fast | `gh run list --workflow=cd.yml --limit 5` |
| Everything is down | `docker compose up --build` → `localhost:8080`, about 2 min |

Ingesting again with a fresh key:

```bash
curl -s -X POST -H "Idempotency-Key: demo-2" \
  $BASE/api/v1/ingest/json-live | tee /tmp/i2.json | jq
BATCH=$(jq -r .batchId /tmp/i2.json); echo $BATCH
```

**Ingest and transform are rate-limited to 5 requests per minute.** A 429
mid-demo is you, not Azure — do not re-run the ingest impatiently.

---

## When it breaks

| Symptom | What to say |
|---|---|
| Slow first call | "F1 free tier, no Always On — a 44-second cold start. The tradeoff we accepted for zero cost." |
| `/health/ready` 503 | "Serverless database resuming from auto-pause. **This is exactly why liveness checks nothing** — a probe that queried the database would make the platform recycle a healthy app." |
| Ingest 502 | "The upstream rate-limited us. The resilience pipeline exhausted its retries and surfaced it as RFC 7807 ProblemDetails, not a 500. Canonical counts are untouched." |
| Replay returns 409 | "Still failing, with the **current** reason. The row stays pending and can be replayed again." |
| Query editor will not sign in | Fall back to the `curl` output |
| Internet down | Screenshots, in order |

Every failure here has an answer that makes you look better. That is not luck —
they are all in the [runbook](runbook.md).

---

## Questions you will get

**"Is this IaaS, PaaS or SaaS?"** PaaS throughout — App Service and Azure SQL are
both platform services. Expect this one almost verbatim.

**"Why not a virtual machine?"** It would mean owning OS patching, TLS
certificates and the web server. We traded runtime control for operational cost.
The real limit: no arbitrary native dependencies.

**"How does it scale?"** Scale-up changes the SKU, scale-out adds instances to
the plan. Volunteer the gap — there is no distributed lock, so more than one
instance would double-process a batch. It is listed as future work.

**"What does it cost?"** Rs. 0. F1 is free, Azure SQL uses the free-limit tier,
and because compute scales to zero an idle system costs storage only.

**"SLA? Disaster recovery?"** F1 carries no SLA; Basic is 99.95%. Single region,
no geo-replication. Real DR would need SQL geo-replication plus Front Door —
scoped out, not overlooked.

**"Why two data-access technologies?"** EF Core for configuration and migrations
where the schema changes; `SqlBulkCopy` and Dapper on the hot path, because EF
round-trips per entity and cannot express a bulk `MERGE`. ADR 002.

**"Why no message queue?"** The staging table *is* the queue — and unlike an
in-memory channel it survives a restart, can be inspected with a `SELECT`, and is
shared across instances. ADR 007.

---

## Do not

- Walk the code file by file. They will ask if they want it.
- Open `localhost`. In a cloud viva it invites the one question you do not want.
- Demo only the happy path. **The dead letter is the project.**
- Say "scalable" without naming the mechanism.
- Quote a number you have not measured.
- Hide the gaps. Naming them first is worth more than hoping nobody asks.

---

## Habits worth more than content

**Pause when a command finishes.** Let them read the output. Two seconds of
silence is fine.

**Say the number; do not read the screen.** "Seventeen thousand seven hundred
rows in six seconds" — never "as you can see here".

See also: [demo.md](demo.md) for the interview-oriented variant,
[setup.md](setup.md) for what a new machine needs, [runbook.md](runbook.md) for
operational failure modes.
