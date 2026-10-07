# ADR 002 — EF Core for configuration, Dapper and SqlBulkCopy for the hot path

**Status:** accepted · **Date:** 2026-10-07

## Context

The service has two very different data access profiles.

**Low volume, rich structure.** Mapping rules, ingest batches, dead letters and
watermarks. Tens to thousands of rows, frequently modelled with relationships,
and they need a migration story because the schema evolves.

**High volume, trivial structure.** Staging rows and canonical readings. A single
archive day is ~17,800 files of several hundred rows each; a live window is
~17,700 readings. These need bulk insert and bulk upsert, and nothing else.

Using one tool for both means accepting a serious compromise in one direction:

- **EF Core everywhere.** `SaveChanges` round-trips per entity and has no way to
  express a `MERGE`. Upserting 100k canonical rows becomes 100k statements —
  unusable at this volume.
- **Dapper everywhere.** Workable, but throws away migrations, change tracking
  and relationship modelling for the configuration tables, where those are
  exactly what make the code pleasant.

## Decision

Split by access profile rather than picking one winner.

| Concern | Tool | Rationale |
|---|---|---|
| Config, metadata, dead letters, watermarks | **EF Core 10** + migrations | Rich modelling and a migration story where the schema actually changes |
| Staging inserts | **`SqlBulkCopy`** | Orders of magnitude faster than `SaveChanges`; this is what the throughput figure rests on |
| Canonical upsert, hot reads | **Dapper** + `MERGE` | EF cannot express an efficient bulk upsert; `MERGE` against the `ExternalKey` unique index is what makes ingestion idempotent |

The boundary is drawn at the table, not scattered per call site: a table is
owned by one approach, and `DataIntegrationDbContext` documents which.

## Consequences

**Good**

- Each workload uses the right tool, with a stated reason.
- Idempotency is achievable: `MERGE` on `ExternalKey` means re-ingesting the same
  readings updates in place rather than duplicating.
- The configuration tables keep migrations, so mapping rules ship with the
  schema instead of being seeded by a script someone forgets to run.

**Costs**

- **Two data-access idioms in one codebase.** A new developer has to learn both,
  and the boundary has to be respected or the codebase degenerates into "use
  whichever was imported". Mitigated by keeping the split table-oriented and
  documenting it on the DbContext.
- Two sets of mapping assumptions can drift — an EF configuration and a
  hand-written `MERGE` column list for the same table must agree. Integration
  tests against real SQL Server (Phase 6) are what catch this; a mocked
  repository never would.
- Raw SQL in `MERGE` statements is not compile-time checked.

## Notes

The costs are real and the second one is the dangerous one. It is still the
right trade: the alternative is either a pipeline too slow to measure, or
abandoning migrations on the tables whose schema changes most.
