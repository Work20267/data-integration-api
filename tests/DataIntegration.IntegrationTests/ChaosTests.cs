using System.Diagnostics.Metrics;
using DataIntegration.Core;
using DataIntegration.Core.Entities;
using DataIntegration.Core.Ingestion;
using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Ingestion;
using DataIntegration.Infrastructure.Observability;
using DataIntegration.Infrastructure.Persistence;
using DataIntegration.Infrastructure.Sources.SensorCommunity;
using DataIntegration.Infrastructure.Transformation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataIntegration.IntegrationTests;

/// <summary>
/// The tests the project exists to pass, run against a real SQL Server.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ChaosTests(SqlServerFixture sql)
{
    private static readonly DateTime Observed = new(2026, 10, 7, 6, 30, 35, DateTimeKind.Utc);

    // Sensor id ranges keep the shared container's rows from colliding between
    // tests, which is cheaper and less brittle than truncating tables.
    private const int IdempotencyBase = 100_000;
    private const int ChaosBase = 200_000;
    private const int ConvergenceBase = 300_000;

    [Fact]
    public async Task Upserting_the_same_observations_twice_produces_no_duplicates()
    {
        var ct = TestContext.Current.CancellationToken;
        const int Rows = 500;

        var payloads = BuildLivePayloads(IdempotencyBase, Rows);

        // First pass.
        await using (var context = sql.CreateContext())
        {
            var batchId = await SeedStagingAsync(context, payloads, SourceKeys.JsonLive, ct);
            var summary = await BuildTransform(context).TransformBatchAsync(batchId, ct);

            Assert.Equal(Rows, summary.RowsCanonical);
            Assert.Equal(0, summary.RowsDeadLettered);
        }

        var afterFirst = await CountAsync(IdempotencyBase, Rows, ct);
        Assert.Equal(Rows, afterFirst.Readings);

        // Second pass: byte-identical payloads, a brand new batch. Batch-level
        // idempotency cannot help here — the MERGE has to do the work.
        await using (var context = sql.CreateContext())
        {
            var batchId = await SeedStagingAsync(context, payloads, SourceKeys.JsonLive, ct);
            await BuildTransform(context).TransformBatchAsync(batchId, ct);
        }

        var afterSecond = await CountAsync(IdempotencyBase, Rows, ct);

        Assert.Equal(afterFirst.Readings, afterSecond.Readings);
        Assert.Equal(afterFirst.Measurements, afterSecond.Measurements);
        Assert.Equal(0, await CountDuplicateKeysAsync(ct));
    }

    [Fact]
    public async Task A_failure_mid_batch_retains_committed_work_and_a_retry_loses_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        const int Rows = 1_000;
        const int ChunkSize = 200;

        var payloads = BuildLivePayloads(ChaosBase, Rows);
        Guid batchId;

        // Fail after two chunks: 400 rows committed, 600 still pending.
        await using (var context = sql.CreateContext())
        {
            batchId = await SeedStagingAsync(context, payloads, SourceKeys.JsonLive, ct);

            var failing = new FailAfterChunksWriter(new CanonicalWriter(context), failAfterChunks: 2);
            var transform = BuildTransform(context, failing, ChunkSize);

            await Assert.ThrowsAsync<TimeoutException>(
                () => transform.TransformBatchAsync(batchId, ct));

            Assert.True(failing.HasFailed);
        }

        var partial = await CountAsync(ChaosBase, Rows, ct);

        // Committed chunks survive the failure. Discarding them would mean
        // re-fetching from upstream, which is what the design exists to avoid.
        Assert.Equal(2 * ChunkSize, partial.Readings);

        await using (var context = sql.CreateContext())
        {
            var remaining = await context.StagingRecords
                .CountAsync(s => s.IngestBatchId == batchId && s.Status == StagingStatus.Pending, ct);

            // Rows in the committed chunks were marked, so a retry skips them.
            Assert.Equal(Rows - (2 * ChunkSize), remaining);
        }

        // Retry with a healthy writer.
        await using (var context = sql.CreateContext())
        {
            var summary = await BuildTransform(context, chunkSize: ChunkSize)
                .TransformBatchAsync(batchId, ct);

            Assert.Equal(Rows - (2 * ChunkSize), summary.RowsRead);
        }

        var final = await CountAsync(ChaosBase, Rows, ct);

        // Zero loss: every row landed.
        Assert.Equal(Rows, final.Readings);

        // Zero duplication: the committed chunks were not written twice.
        Assert.Equal(0, await CountDuplicateKeysAsync(ct));
    }

    [Fact]
    public async Task The_same_observation_from_both_sources_converges_on_one_row()
    {
        // The guarantee Phase 5 could not demonstrate against the live upstream,
        // because the archive publishes a day behind. With controlled payloads it
        // is directly assertable: the same sensor and second, described by two
        // sources whose formats agree on nothing, must be one canonical row.
        var ct = TestContext.Current.CancellationToken;
        const int Rows = 100;

        var live = BuildLivePayloads(ConvergenceBase, Rows);
        var archive = BuildArchivePayloads(ConvergenceBase, Rows);

        await using (var context = sql.CreateContext())
        {
            var batchId = await SeedStagingAsync(context, live, SourceKeys.JsonLive, ct);
            await BuildTransform(context).TransformBatchAsync(batchId, ct);
        }

        var afterLive = await CountAsync(ConvergenceBase, Rows, ct);
        Assert.Equal(Rows, afterLive.Readings);

        await using (var context = sql.CreateContext())
        {
            var batchId = await SeedStagingAsync(context, archive, SourceKeys.CsvArchive, ct);
            var summary = await BuildTransform(context, rules: TestData.ArchiveRules())
                .TransformBatchAsync(batchId, ct);

            Assert.Equal(Rows, summary.RowsCanonical);
            Assert.Equal(0, summary.RowsDeadLettered);
        }

        var afterArchive = await CountAsync(ConvergenceBase, Rows, ct);

        // Two sources, one row each. Not two.
        Assert.Equal(Rows, afterArchive.Readings);
        Assert.Equal(0, await CountDuplicateKeysAsync(ct));

        await using var verify = sql.CreateContext();

        // The archive wrote last, so it owns LastSourceKey — but the country the
        // archive does not carry must survive from the live feed.
        var sample = await verify.CanonicalReadings
            .AsNoTracking()
            .FirstAsync(r => r.SensorId == ConvergenceBase, ct);

        Assert.Equal(SourceKeys.CsvArchive, sample.LastSourceKey);
        Assert.Equal("DE", sample.Country);
    }

    // ---- helpers ----------------------------------------------------------

    private static List<RawRow> BuildLivePayloads(int sensorBase, int count) =>
        [.. Enumerable.Range(0, count).Select(i => new RawRow(
            SourceKeys.JsonLive,
            "SDS011",
            TestData.LivePayload(sensorBase + i, Observed.AddSeconds(i), 10.5m)))];

    private static List<RawRow> BuildArchivePayloads(int sensorBase, int count) =>
        [.. Enumerable.Range(0, count).Select(i => new RawRow(
            SourceKeys.CsvArchive,
            "SDS011",
            TestData.ArchivePayload(sensorBase + i, Observed.AddSeconds(i), 10.5m)))];

    private static async Task<Guid> SeedStagingAsync(
        DataIntegrationDbContext context,
        IReadOnlyList<RawRow> rows,
        string sourceKey,
        CancellationToken ct)
    {
        var batch = new IngestBatch
        {
            Id = Guid.CreateVersion7(),
            SourceKey = sourceKey,
            IdempotencyKey = Guid.NewGuid().ToString(),
            StartedAtUtc = DateTime.UtcNow,
            Status = BatchStatus.Completed,
            RowsReceived = rows.Count,
        };

        context.IngestBatches.Add(batch);
        await context.SaveChangesAsync(ct);

        await new StagingWriter(context).WriteChunkAsync(batch.Id, rows, ct);

        return batch.Id;
    }

    private static TransformService BuildTransform(
        DataIntegrationDbContext context,
        ICanonicalWriter? canonicalWriter = null,
        int chunkSize = 500,
        MappingRuleSet? rules = null)
    {
        var meterFactory = new NoOpMeterFactory();

        return new TransformService(
            [new LiveFeedPayloadParser(), new CsvArchivePayloadParser()],
            new StaticMappingRuleProvider(rules ?? TestData.LiveRules()),
            new StagingReader(context),
            canonicalWriter ?? new CanonicalWriter(context),
            new DeadLetterStore(context),
            new TransformMetrics(meterFactory),
            Options.Create(new TransformOptions { ChunkSize = chunkSize }),
            NullLogger<TransformService>.Instance);
    }

    private async Task<(int Readings, int Measurements)> CountAsync(
        int sensorBase, int count, CancellationToken ct)
    {
        await using var context = sql.CreateContext();

        var upper = sensorBase + count;

        var readings = await context.CanonicalReadings
            .CountAsync(r => r.SensorId >= sensorBase && r.SensorId < upper, ct);

        var measurements = await context.CanonicalMeasurements
            .CountAsync(m => m.Reading!.SensorId >= sensorBase && m.Reading.SensorId < upper, ct);

        return (readings, measurements);
    }

    private async Task<int> CountDuplicateKeysAsync(CancellationToken ct)
    {
        await using var context = sql.CreateContext();

        return await context.CanonicalReadings
            .GroupBy(r => r.ExternalKey)
            .CountAsync(g => g.Count() > 1, ct);
    }
}

/// <summary>Metrics are not under test here; this keeps the meter out of the way.</summary>
internal sealed class NoOpMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (var meter in _meters)
        {
            meter.Dispose();
        }

        _meters.Clear();
    }
}
