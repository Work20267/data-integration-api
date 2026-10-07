# ADR 004 — Stream ingestion into bounded chunks

**Status:** accepted · **Date:** 2026-10-07

## Context

One call to the live feed returns about **8.5 MB and 17,770 readings**, measured
directly. The daily archive is larger again: ~17,800 files of several hundred
rows each.

The obvious implementation reads the whole response, deserialises it into a list,
and writes the list:

```csharp
var json = await http.GetStringAsync(url);          // 8.5 MB string
var records = JsonSerializer.Deserialize<List<Record>>(json);  // + object graph
await writer.WriteAsync(records);                    // one huge operation
```

Three problems with that, in increasing order of seriousness:

1. Peak memory is the whole response plus the whole object graph, for no benefit.
2. Nothing is persisted until everything has been fetched and parsed, so a
   failure at 90% loses 90% of the work.
3. It does not degrade. A feed twice the size needs twice the memory, and the
   archive — orders of magnitude larger — would not fit at all.

## Decision

Stream end to end, and bound the only buffer that exists.

| Stage | Mechanism |
|---|---|
| HTTP | `HttpCompletionOption.ResponseHeadersRead` — parsing begins while the body is still arriving |
| Parsing | `JsonSerializer.DeserializeAsyncEnumerable<JsonElement>` — one array element at a time |
| Reader contract | `IAsyncEnumerable<RawRow>`, so the source cannot force materialisation on the caller |
| Persistence | Buffer to `StagingChunkSize` (2,000), `SqlBulkCopy` the chunk, clear, repeat |

Peak memory is therefore one chunk, not one response — and it is the same for a
5-minute window as for a year of archive.

Each element is stored with `JsonElement.GetRawText()`, which preserves the
source bytes exactly rather than round-tripping through a model and
re-serialising.

A record cap (`MaxRecordsPerCall`) exists as a circuit breaker on the shape of
the data, not on its size: a normal window is ~17,700, so an order-of-magnitude
larger response means something upstream changed and stopping is correct.

## Consequences

**Good**

- Flat memory regardless of volume. Measured 17,770 rows in 15.51 s end to end.
- Partial progress survives failure. When the upstream dies mid-stream the
  chunks already written stay in staging, the batch is marked failed with its
  real row count, and the work is reprocessable. Covered by
  `Upstream_failure_marks_the_batch_failed_and_keeps_the_rows_already_staged`.
- The same reader contract works unchanged for the archive in Phase 5, where
  materialising was never an option.

**Costs**

- More round trips: 17,770 rows is nine `SqlBulkCopy` calls rather than one.
  Measurably not the bottleneck here — the 8.5 MB download dominates.
- A failed batch leaves rows in staging that belong to no completed batch.
  That is deliberate, but it means staging needs a retention policy eventually,
  or it grows without bound.
- `IAsyncEnumerable` makes the reader harder to test than a function returning a
  list. Mitigated by the fake reader in the unit tests, which also uses it to
  inject a failure at a chosen row.

## Notes

`StagingChunkSize` is configuration, not a constant, because the right value
depends on row size — and archive rows are far smaller than live-feed JSON
objects. It lives in `IngestOptions` rather than the source's own options, so
the pipeline does not depend on any particular source's configuration.
