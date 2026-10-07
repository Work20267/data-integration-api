# ADR 006 — Two sources, one canonical model, and how idempotency was proven

**Status:** accepted · **Date:** 2026-10-07

## Context

Phases 1–4 built the pipeline around one source. The second source is the test
of whether "adding a source is configuration" was a real design property or a
comment in a file.

The two sources share a domain and almost nothing else:

| | `json-live` | `csv-archive` |
|---|---|---|
| Transport | One HTTP GET, ~8.5 MB | A 4.5 MB directory listing, then one GET per sensor |
| Shape | Nested JSON; measurements in a `sensordatavalues` array of `{value_type, value}` | Flat semicolon-delimited CSV, **different columns per device** |
| Timestamp | `2026-10-04 00:00:15` | `2026-10-04T00:00:15` |
| Sea-level pressure | `pressure_at_sealevel` | `pressure_sealevel` |
| Carries country / indoor flag | yes | no |

## Decision

### Adding the source touched three things

1. `CsvArchivePayloadParser` — a new `IPayloadParser`
2. `ArchiveReader` — a new `IUpstreamReader`
3. Two DI registrations

**The mapping engine, the canonical writer, the dead-letter store, the transform
service and the ingest service were not modified at all.** The nine
`csv-archive` mapping rules had already been seeded in Phase 2, before the
parser existed.

Result on the first run: **4,560 rows → 4,560 canonical, zero dead-lettered.**

### The CSV parser knows no sensor model

Column layouts differ per device. Rather than enumerate them, the parser reads
the **header row** for field names, strips the six metadata columns
(`sensor_id`, `sensor_type`, `location`, `lat`, `lon`, `timestamp`) and offers
everything else to the mapping rules. A device nobody has seen parses fine;
whether its fields *mean* anything is a separate question, answered by rules.

### Each staged row carries its own header

A payload is two lines — the file's header, then one data row. A staged row is
therefore self-describing and replayable years later without the file it came
from still existing.

### Nulls mean "this source does not say"

The archive has no country or indoor flag. The parser emits null rather than a
default, and the `MERGE` overwrites only what it was given — so the live feed's
country survives an archive update of the same reading.

## How idempotency was actually proven

The intended demonstration — ingest the archive for a day already streamed live,
observe zero new rows — **cannot be run on demand.** The archive publishes a day
behind, so a day with live coverage is not available until tomorrow.

The attempt also hit a real obstacle: a second archive ingest returned

```
HTTP 502 {"title":"Upstream unavailable",
          "detail":"Source 'csv-archive' could not be reached after retries."}
```

The volunteer-funded host rate-limited us. That is itself a result worth
recording — the resilience pipeline exhausted its retries, the failure surfaced
as RFC 7807 ProblemDetails rather than a 500 or a hang, and **canonical counts
were untouched.** The Phase 3 "survives upstream failure" gate was met against a
genuine failure rather than a simulated one.

So the property was isolated instead, at zero cost to the upstream: reset the
same 4,560 staged rows to pending and re-transform them.

```
rowsCanonical reported        4,560        the writer did the work
readings      22,163 → 22,163              UNCHANGED
measurements  58,975 → 58,975              UNCHANGED
duplicate ExternalKeys            0
```

That tests exactly the mechanism under question — the `MERGE` against the
`ExternalKey` unique index — without conflating it with HTTP behaviour. The
cross-source case runs the identical code path: the writer never knows which
source produced a reading.

A unit test pins the remaining half of the claim, that both parsers derive the
*same* key from their differently formatted timestamps:
`Both_sources_produce_the_same_external_key_for_the_same_observation`.

## Consequences

**Good**

- The parser/engine seam held under a source that shares no shape with the first.
- 4,560 archive rows produced zero dead letters, using rules written before the
  parser existed.
- Idempotency is proven at the layer that provides it, and the proof is
  repeatable without touching the network.

**Costs and open points**

- **The archive is unreliable and rate-limits.** Hence `ArchiveMaxFiles` (default
  25), a 300 ms inter-request pause, and a `User-Agent` identifying us. Treat any
  larger backfill as needing an explicit conversation with the operators.
- **The listing itself is flaky.** One fetch returned 280 KB instead of 4.5 MB
  and contained only `bme280` entries — which is why all 4,560 archive readings
  are BME280 despite the filter also allowing `sds011`. A truncated listing
  currently looks like a small day rather than an error. Worth detecting.
- The cross-source overlap remains unobserved in production data, by nature of
  the publishing delay. Phase 6's harness should assert it against a controlled
  upstream rather than waiting on the calendar.
- `ArchiveDate` is configuration, so backfilling a range means restarting with a
  new value. A date parameter on the ingest endpoint is the obvious improvement.
