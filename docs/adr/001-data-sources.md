# ADR 001 — Sensor.Community as the two integration sources

**Status:** accepted · **Date:** 2026-10-07

## Context

The service needs two upstream sources to be a genuine integration project
rather than a CRUD application with extra steps. If both ends of the pipeline
are schemas I invented, nothing is being integrated and the failure handling is
simulated.

Requirements for the sources:

- Free, no API key, no signup — friction here stalls the whole project
- Genuinely messy: gaps, nulls, duplicates, inconsistent schemas, clock skew
- **Two different formats**, so transformation is real work
- The same domain, so a single canonical model is meaningful
- Enough volume to make `SqlBulkCopy` and a throughput figure worth measuring

Options considered:

| Option | Rejected because |
|---|---|
| Open-Meteo weather API | Too clean. Well-formed data gives the error handling nothing to do. |
| GTFS-Realtime transit feeds | Protobuf parsing adds work that teaches nothing about integration, and most agency feeds need keys. |
| Government open data portals | Good messiness, but requires an API key and the datasets are unrelated to each other. |
| Invented business data | No. See Context. |

## Decision

Use the two **Sensor.Community** feeds: the live JSON API
(`data.sensor.community/static/v2/data.json`) and the daily CSV archive
(`archive.sensor.community/<date>/`).

Both endpoints were verified before deciding — 17,762 readings in a 5-minute
window, 17,864 archive files in a day, no authentication on either.

Three properties decided it:

1. **Structurally different.** The JSON nests measurements as a
   `sensordatavalues` array of `{value_type, value}` pairs; the CSV is flat
   columns that differ per sensor model (`sds011` carries `P1;durP1;ratioP1;P2…`,
   `bme280` carries `pressure;altitude;pressure_sealevel;temperature;humidity`).
   A config-driven mapping layer is required, not decorative — without it there
   would be a hardcoded branch per sensor model.

2. **The field names already disagree across sources.** The same measurement is
   `pressure_at_sealevel` in the JSON feed and `pressure_sealevel` in the CSV.
   One fact, and it justifies the entire `SourceMapping` table.

3. **They overlap deliberately.** The archive is where the live feed lands a day
   later, so the two sources contain the same readings. Ingesting Monday's
   archive after streaming Monday live must produce zero new rows. Idempotency
   becomes a requirement imposed by the data rather than a test I invented.

## Consequences

**Good**

- Authentic messiness: hobbyist ESP8266 hardware produces dropouts, clock skew,
  duplicate pushes and nonsense values (an observed `humidity;100.00` from a
  saturated sensor), plus empty fields (`;;`) and semicolon delimiters.
- Idempotency is exercised by normal operation, not a synthetic case.
- Volume supports a real throughput measurement.

**Costs and risks**

- The upstream is volunteer-funded. Ingestion must throttle, cache locally and
  never enumerate all 17,864 files in a day. This is why the Polly pipeline and
  rate limiter exist.
- Schema is undocumented in places and could change without notice. The staging
  table holding raw payloads is the mitigation: a mapping break is fixable
  without re-fetching.
- **The `P1`/`P2` trap.** In this schema `P1` is PM10 and `P2` is PM2.5; `P1` is
  not PM1, which is `P0`. A naive implementation maps by name and is silently
  wrong. Encoded explicitly in the seeded mapping rows.

## Attribution

Data is ODbL / CC-BY-SA and attributed in the README.
