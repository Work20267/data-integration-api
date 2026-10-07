# ADR 005 — Per-row isolation, and the dead letter as a work queue

**Status:** accepted · **Date:** 2026-10-07

## Context

The first real transformation run was the test of this decision, not a
hypothetical:

```
17,770 read → 17,707 canonical, 63 dead-lettered, 3.49s
```

All 63 failures had one cause, and it was not corrupt data:

> *No field on this **Radiation Si22G** reading resolved to a known metric.*

Someone has a Geiger counter on an air-quality sensor network. Its fields —
`counts`, `counts_per_minute`, `hv_pulses`, `sample_time_ms` — had no mapping
rules, because nobody anticipated radiation when writing them.

A batch-level failure model has to choose one of two wrong answers for this:

- **Reject the batch.** 63 unexpected rows out of 17,770 destroy 17,707 good
  observations. Worse, the cause was a *configuration gap*, so the batch would
  keep failing until someone noticed.
- **Silently skip unmapped rows.** The batch succeeds, the data is quietly
  incomplete, and nobody ever learns the Geiger counter exists.

Neither is acceptable. The data is produced by hobbyist firmware on a volunteer
network: unanticipated input is not an edge case, it is the normal condition.

## Decision

**Transform every row inside its own failure boundary, and treat a partial
outcome as success.**

Three parts:

1. **Per-row isolation.** `TransformService.TransformRow` catches *everything*,
   including exceptions nobody predicted, and converts each to one dead letter.
   The method's contract is that it never throws. The `CA1031` suppression on it
   is deliberate and annotated — a blanket catch is normally a smell, and here it
   is the entire point.

2. **Both counts are reported.** `TransformSummary` carries `RowsCanonical` and
   `RowsDeadLettered` side by side, and the endpoint returns 200 for a partial
   batch. "17,707 canonical, 63 dead-lettered" is a success; a caller that only
   saw a status code would not know that.

3. **The dead letter is a queue, not a graveyard.** Each row keeps its raw
   payload — preserved at ingest, per ADR 004 — plus a stable `ErrorCode` and the
   message. `POST /api/v1/dead-letter/{id}/replay` re-transforms it against the
   *current* rules.

The operator loop, executed end to end against the real data:

```
INSERT INTO SourceMapping (SourceKey, SensorModel, SourceField, MetricCode, ...)
VALUES ('json-live', 'Radiation Si22G', 'counts_per_minute', 'RADIATION_CPM', ...)

POST /api/v1/transform/reload-mappings   → 204
POST /api/v1/dead-letter/1/replay        → 200 {"outcome":"replayed"}
```

No code change. No deployment. No second request to the upstream.

## Consequences

**Good**

- One unexpected sensor costs 63 dead letters, not 17,707 observations.
- The failure is *visible and actionable*: `ErrorCode` is indexed, and
  `GET /dead-letter/summary` groups by it, so an operator sees immediately
  whether 400 failures are one problem or four. All 63 here were one.
- A replay that still fails returns 409 with the *current* reason and leaves the
  row pending. Verified accidentally and usefully: an INSERT silently failed on
  a `QUOTED_IDENTIFIER` setting, and replay correctly reported the row as still
  unmapped rather than claiming success.
- Rules can be scoped to a sensor model, so `counts_per_minute` means something
  on a Geiger counter and nothing anywhere else.

**Costs**

- **The blanket catch can hide a real bug.** A genuine defect in the engine
  would show up as thousands of dead letters rather than a loud failure. The
  mitigation is that `ErrorCode` grouping makes a sudden spike obvious, and the
  exception type is recorded in the message — but this is a real trade and worth
  watching.
- Staging rows for rejected payloads are retained indefinitely, as are dead
  letters. Both need a retention policy before this runs for long.
- Replay is one row per call. Fine for 63; a bulk replay endpoint will be needed
  the first time a rule fixes thousands.

## Known gap this run exposed

The canonical data now contains values that are physically impossible:

| Metric | Observed range |
|---|---|
| `TEMPERATURE` | −149.88 … 614.90 °C |
| `HUMIDITY` | −6.00 … 1664.00 % |
| `PRESSURE` | −21,765 … 9,999,571 Pa |
| `PM2_5` / `PM10` | minimum −1.00 |

The pipeline is ingesting faithfully, which is correct — but there is no
plausibility check, so a broken sensor's output is stored as though it were an
observation. The natural fix is min/max bounds per metric on `SourceMapping` and
a `VALUE_OUT_OF_RANGE` error code, which would route these to the dead letter
through the machinery this ADR already establishes. Not yet implemented, and
recorded here so it is not mistaken for an oversight.
