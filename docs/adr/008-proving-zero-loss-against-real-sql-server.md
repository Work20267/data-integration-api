# ADR 008 — Proving zero loss against a real SQL Server

**Status:** accepted · **Date:** 2026-10-07

## Context

Two claims carry this project, and neither can be proven with a mock:

1. **Idempotency.** Processing the same observation twice produces one row.
2. **Zero loss.** A failure mid-batch retains committed work, and a retry
   completes without duplicating it.

Both are properties of **SQL Server** — of `MERGE`, of `HOLDLOCK`, of a unique
index, of what a transaction commits before a failure. A faked
`ICanonicalWriter` asserts them against my own assumptions about those
mechanisms, which is exactly the thing in doubt. Unit tests with fakes prove the
orchestration is correct *given* a writer that behaves as I believe; they cannot
prove the writer does.

## Decision

Run the critical tests against a **real SQL Server in a container**, migrated
with the same migrations CD applies to Azure.

`SqlServerFixture` starts one `Testcontainers.MsSql` instance per test run,
shared via a collection fixture, and runs `Database.MigrateAsync()`. Tests
separate themselves by sensor-id range rather than by truncating tables, which
is cheaper and less brittle than resetting shared state.

### Three tests

**1 · The same observations twice produce no duplicates.** 500 rows, staged and
transformed; then byte-identical payloads in a *brand new batch*, so batch-level
idempotency cannot help and the `MERGE` has to do the work. Reading and
measurement counts must be unchanged, and duplicate `ExternalKey` groups must be
zero.

**2 · A failure mid-batch loses nothing.** 1,000 rows at a chunk size of 200,
with a writer that throws after two chunks. Asserted in order:

- 400 readings are committed and survive the exception
- exactly 600 staging rows remain `Pending` — the committed ones were marked, so
  a retry skips them
- a retry processes exactly those 600
- final count is 1,000, with **zero** duplicate keys

That is zero loss and zero duplication, stated as numbers rather than adjectives.

**3 · Both sources converge on one row.** 100 observations ingested from the
live JSON format, then the *same* 100 from the archive's CSV format — different
delimiter, different timestamp layout, no country column. The result must be 100
rows, not 200.

This is the guarantee Phase 5 could not demonstrate against the live upstream,
because the archive publishes a day behind. With controlled payloads it is
directly assertable instead of waiting on a calendar.

### Why a deterministic failure rather than killing the container

The fixture *can* stop and restart the container, and doing so would test
connection loss and recovery. It would also make the test depend on timing — on
the kill landing between two specific chunks — and a flaky test is worse than no
test. I deleted a flaky test in Phase 3 for precisely this reason.

`FailAfterChunksWriter` fails after a chosen number of chunks instead: the same
invariant, asserted the same way on every run. The container-stop capability is
kept on the fixture for manual investigation but is not part of the suite.

## What this caught

Writing test 3 exposed a real bug.

`CsvArchivePayloadParser` emits `Country = null` and `IsIndoor = null`, with a
comment stating that null means *"this source does not say"* and that the upsert
should leave whatever the other source supplied. The `MERGE` did not implement
that:

```sql
target.Country = source.Country   -- overwrites "DE" with NULL
```

Ingesting the archive after the live feed would have silently erased the country
and indoor flag on every overlapping reading. Nothing else would have failed —
no exception, no dead letter, no count discrepancy. It would have looked like
clean data.

Fixed by distinguishing "not supplied" from "now unknown":

```sql
target.Country   = COALESCE(source.Country,   target.Country),
target.IsIndoor  = COALESCE(source.IsIndoor,  target.IsIndoor),
target.LocationId = COALESCE(source.LocationId, target.LocationId),
target.Latitude  = COALESCE(source.Latitude,  target.Latitude),
target.Longitude = COALESCE(source.Longitude, target.Longitude),
```

Test 3 now asserts it directly: after the archive overwrites a live reading,
`LastSourceKey` is `csv-archive` **and** `Country` is still `DE`.

## Consequences

**Good**

- The two headline claims are now evidence, not assertion, and the evidence is
  reproducible by anyone with Docker.
- A silent data-loss bug was found before it reached production data.
- The schema under test is the migrated schema, so a migration that breaks the
  `MERGE` fails here rather than in Azure.
- GitHub-hosted runners provide Docker, so these run in CI unchanged.

**Costs**

- The integration suite now takes about a minute on a cold run, against
  milliseconds for the unit tests. Acceptable for what it buys, but it means CI
  time is no longer negligible.
- Tests share one container and separate by id range. That is deliberate, and it
  does mean a test asserting a *global* count would be wrong — `CountAsync` is
  scoped to a range for this reason.
- Connection-loss recovery is still unproven. The deterministic failure covers
  the invariant that matters; a genuine network partition does not.
