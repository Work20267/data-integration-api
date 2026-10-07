using DataIntegration.Core;
using DataIntegration.Core.Entities;
using DataIntegration.Core.Ingestion;
using DataIntegration.Core.Sync;
using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Ingestion;
using DataIntegration.Infrastructure.Observability;
using DataIntegration.Infrastructure.Sync;
using DataIntegration.Infrastructure.Transformation;
using DataIntegration.UnitTests.Ingestion;
using DataIntegration.UnitTests.Transformation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataIntegration.UnitTests.Sync;

public sealed class SyncCycleTests
{
    private static (SyncCycle Cycle, FakeSyncWatermarkStore Watermarks, FakeIngestBatchStore Batches)
        Build(IUpstreamReader reader, FakeIngestBatchStore? batches = null)
    {
        using var meterFactory = new TestMeterFactory();

        batches ??= new FakeIngestBatchStore();

        var ingest = new IngestService(
            [reader],
            batches,
            new FakeStagingWriter(),
            new IngestMetrics(meterFactory),
            Options.Create(new IngestOptions { StagingChunkSize = 100 }),
            NullLogger<IngestService>.Instance);

        var transform = new TransformService(
            [new ScriptedParser()],
            new FakeMappingRuleProvider(new MappingRuleSet(SourceKeys.JsonLive, [])),
            new FakeStagingReader([]),
            new FakeCanonicalWriter(),
            new FakeDeadLetterStore(),
            new TransformMetrics(meterFactory),
            Options.Create(new TransformOptions()),
            NullLogger<TransformService>.Instance);

        var watermarks = new FakeSyncWatermarkStore();

        var cycle = new SyncCycle(
            ingest, transform, watermarks, NullLogger<SyncCycle>.Instance);

        return (cycle, watermarks, batches);
    }

    [Fact]
    public async Task A_failing_cycle_returns_a_result_instead_of_throwing()
    {
        // This is what keeps the worker alive. An exception escaping a
        // BackgroundService stops it silently or brings the host down — either
        // way one bad cycle would end all future syncing.
        var reader = new FakeUpstreamReader(SourceKeys.JsonLive, 10) { ThrowAfter = 0 };
        var (cycle, watermarks, _) = Build(reader);

        var result = await cycle.RunAsync(
            SourceKeys.JsonLive, "key-1", TestContext.Current.CancellationToken);

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Contains("HttpRequestException", result.Error, StringComparison.Ordinal);

        // A failed cycle must not claim progress it did not make.
        Assert.Empty(watermarks.Advanced);
    }

    [Fact]
    public async Task A_successful_cycle_advances_the_watermark()
    {
        var reader = new FakeUpstreamReader(SourceKeys.JsonLive, 5);
        var (cycle, watermarks, _) = Build(reader);

        var result = await cycle.RunAsync(
            SourceKeys.JsonLive, "key-2", TestContext.Current.CancellationToken);

        Assert.Equal(SyncOutcome.Completed, result.Outcome);
        Assert.Equal(5, result.RowsStaged);
        Assert.Equal([SourceKeys.JsonLive], watermarks.Advanced);
    }

    [Fact]
    public async Task An_already_ingested_window_is_skipped_without_touching_upstream()
    {
        var batches = new FakeIngestBatchStore();
        batches.SeedExisting(new IngestBatch
        {
            Id = Guid.CreateVersion7(),
            SourceKey = SourceKeys.JsonLive,
            IdempotencyKey = "sync:json-live:2026-10-07T06:30",
            RowsReceived = 17_770,
            Status = BatchStatus.Completed,
        });

        var reader = new FakeUpstreamReader(SourceKeys.JsonLive, 100);
        var (cycle, watermarks, _) = Build(reader, batches);

        var result = await cycle.RunAsync(
            SourceKeys.JsonLive,
            "sync:json-live:2026-10-07T06:30",
            TestContext.Current.CancellationToken);

        Assert.Equal(SyncOutcome.SkippedIdempotent, result.Outcome);

        // Two overlapping triggers — a manual call during a scheduled cycle, or a
        // second instance — must not cost the upstream a second 8.5 MB fetch.
        Assert.False(reader.WasRead);
        Assert.Empty(watermarks.Advanced);
    }

    [Fact]
    public async Task Cancellation_propagates_rather_than_being_reported_as_failure()
    {
        // Shutdown is not a failure. Swallowing it would make the host wait for a
        // worker that is pretending nothing happened.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var reader = new FakeUpstreamReader(SourceKeys.JsonLive, 10);
        var (cycle, _, _) = Build(reader);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cycle.RunAsync(SourceKeys.JsonLive, "key-3", cts.Token));
    }
}
