# ADR 007 — The staging table is the queue

**Status:** accepted · **Date:** 2026-10-07

## Context

Scheduled sync needs ingest and transform to run one after the other, repeatedly.
The reflexive design puts a queue between them:

```
reader ──▶ bounded Channel<RawRow> ──▶ transform worker
```

A bounded `System.Threading.Channels.Channel<T>` is the idiomatic .NET answer and
gives real backpressure: when the consumer falls behind, the producer blocks
instead of growing memory without limit. It is a good pattern, it is what I
originally planned, and it is the wrong choice here.

The pipeline already has a buffer between those two stages — the
`StagingRecord` table. Ingest writes to it; transform reads from it. Adding a
channel would put a second buffer beside the first.

## Decision

**No in-memory queue. The staging table is the queue.**

`SyncWorker` runs `SyncCycle` per source on a `PeriodicTimer`: ingest writes raw
rows to staging, transform drains staging, the watermark advances. Nothing is
held in memory between the two stages.

### Why the table wins

| | Staging table | Bounded channel |
|---|---|---|
| Survives a restart | **yes** | no — in-flight rows are lost |
| Survives a crash mid-batch | **yes** | no |
| Observable | `SELECT … WHERE Status = 0` | a debugger, at best |
| Shared across instances | **yes** | no, per-process |
| Backpressure | chunk size bounds memory | bounds memory |
| Extra moving part | none | one |

The decisive column is the first. A channel would mean that restarting the app
mid-ingest discards rows already fetched from a volunteer-funded upstream — the
exact cost ADR 004 preserves the raw payload to avoid.

Backpressure, the channel's main attraction, is already provided: ingest buffers
at most `StagingChunkSize` rows (2,000) before flushing, and transform reads one
page at a time. Memory is bounded by chunk size, not by volume, whether or not a
channel exists.

### What the worker owns

Timing and scoping, and nothing else:

- **A scope per source per cycle.** `IngestService` and `TransformService` hold a
  `DbContext`; one long-lived context across every cycle would grow its change
  tracker without bound.
- **A startup delay** (30 s default) so the first cycle does not compete with the
  first real requests, and a crash-looping instance cannot hammer the upstream.
- **An idempotency key derived from the minute**, so a manual call during a
  scheduled cycle — or a second instance — is recognised as a replay rather than
  costing another 8.5 MB fetch.
- **`TimeProvider` injected**, so timing is testable without elapsed real time.

### Off by default

`Sync:Enabled` defaults to `false`. The App Service free tier allows 60
CPU-minutes per day; a loop polling an 8.5 MB feed would exhaust it and the site
would begin returning 403. It is also rude to poll a volunteer-funded upstream
from an idle demo. Enabling it should be a decision.

### Failure is a return value

`SyncCycle.RunAsync` never throws except on cancellation. An exception escaping
a `BackgroundService` stops it silently, or brings the host down, depending on
`BackgroundServiceExceptionBehavior` — either way **one bad cycle would end all
future syncing**. So a failure returns `SyncOutcome.Failed`, the worker logs it
and carries on, and the next tick retries. A source that is down does not stop
the others.

Cancellation is deliberately excluded from that catch: shutdown is not failure,
and swallowing it makes the host wait on a worker pretending nothing happened.

## Consequences

**Good**

- Restart safety comes free, because the buffer was always durable.
- Pending work is inspectable with a query, which matters at 3 a.m.
- One fewer concurrency primitive to reason about.
- The failure path is pinned by tests: a throwing reader yields `Failed` and
  leaves the watermark untouched; a cancelled token propagates.

**Costs**

- **Higher latency than an in-memory handoff.** Rows go to disk before being
  transformed. Irrelevant for a 5-minute publishing window; it would matter for
  a sub-second pipeline, and that is the point at which this decision should be
  revisited.
- **Database write amplification.** Every row is written twice — once to staging,
  once to canonical — plus the status update. Accepted for the durability.
- **No cross-instance coordination.** Two instances with sync enabled would both
  run cycles. The per-minute idempotency key makes the collision harmless rather
  than duplicating data, but it is luck rather than design; a real multi-instance
  deployment wants a distributed lock.
- Staging rows accumulate. A retention policy is still missing — recorded in
  ADR 005 and still true.

## When to revisit

If a source ever becomes a continuous stream rather than a polled window — a
message broker, a webhook firehose — the staging-table-as-queue shape stops
fitting, and Azure Service Bus between ingest and transform becomes the right
answer. Nothing here forecloses that: `IStagingWriter` and `IStagingReader` are
the seam it would replace.
