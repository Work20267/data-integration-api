using DataIntegration.Core;
using DataIntegration.Core.Entities;
using DataIntegration.Core.Ingestion;
using DataIntegration.Infrastructure.Ingestion;
using DataIntegration.Infrastructure.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataIntegration.UnitTests.Ingestion;

public sealed class IngestServiceTests
{
    private static IngestService Build(
        IUpstreamReader reader,
        IStagingWriter staging,
        IIngestBatchStore batches,
        int chunkSize = 2_000)
    {
        using var meterFactory = new TestMeterFactory();

        return new IngestService(
            [reader],
            batches,
            staging,
            new IngestMetrics(meterFactory),
            Options.Create(new IngestOptions { StagingChunkSize = chunkSize }),
            NullLogger<IngestService>.Instance);
    }

    [Fact]
    public async Task Streams_rows_into_chunks_of_the_configured_size()
    {
        var reader = new FakeUpstreamReader(SourceKeys.JsonLive, rowCount: 5);
        var staging = new FakeStagingWriter();
        var service = Build(reader, staging, new FakeIngestBatchStore(), chunkSize: 2);

        var result = await service.IngestAsync(
            SourceKeys.JsonLive, "key-1", TestContext.Current.CancellationToken);

        // 5 rows at a chunk size of 2 must be three round trips, not one of five:
        // the point of streaming is that memory stays bounded regardless of volume.
        Assert.Equal([2, 2, 1], staging.ChunkSizes);
        Assert.Equal(5, result.RowsStaged);
        Assert.False(result.WasAlreadyProcessed);
    }

    [Fact]
    public async Task Writes_a_single_chunk_when_rows_fit_within_it()
    {
        var reader = new FakeUpstreamReader(SourceKeys.JsonLive, rowCount: 3);
        var staging = new FakeStagingWriter();
        var service = Build(reader, staging, new FakeIngestBatchStore(), chunkSize: 10);

        await service.IngestAsync(SourceKeys.JsonLive, "key-2", TestContext.Current.CancellationToken);

        Assert.Equal([3], staging.ChunkSizes);
    }

    [Fact]
    public async Task Replaying_an_idempotency_key_does_not_read_upstream_again()
    {
        var batches = new FakeIngestBatchStore();
        batches.SeedExisting(new IngestBatch
        {
            Id = Guid.CreateVersion7(),
            SourceKey = SourceKeys.JsonLive,
            IdempotencyKey = "already-done",
            RowsReceived = 17_770,
            Status = BatchStatus.Completed,
        });

        var reader = new FakeUpstreamReader(SourceKeys.JsonLive, rowCount: 100);
        var staging = new FakeStagingWriter();
        var service = Build(reader, staging, batches);

        var result = await service.IngestAsync(
            SourceKeys.JsonLive, "already-done", TestContext.Current.CancellationToken);

        Assert.True(result.WasAlreadyProcessed);
        Assert.Equal(17_770, result.RowsStaged);

        // The upstream must not be touched at all — that is the whole point of
        // idempotency here, given the source is volunteer-funded.
        Assert.False(reader.WasRead);
        Assert.Empty(staging.ChunkSizes);
    }

    [Fact]
    public async Task An_unknown_source_throws_before_a_batch_is_opened()
    {
        var batches = new FakeIngestBatchStore();
        var service = Build(
            new FakeUpstreamReader(SourceKeys.JsonLive, 1), new FakeStagingWriter(), batches);

        await Assert.ThrowsAsync<UnknownSourceException>(() =>
            service.IngestAsync("no-such-source", "key-3", TestContext.Current.CancellationToken));

        // No batch row should exist for a request that was never valid.
        Assert.Null(batches.Completed);
        Assert.Null(batches.Failed);
    }

    [Fact]
    public async Task Upstream_failure_marks_the_batch_failed_and_keeps_the_rows_already_staged()
    {
        // Fails on row 5 with a chunk size of 2, so two chunks (4 rows) are
        // already committed when the exception lands.
        var reader = new FakeUpstreamReader(SourceKeys.JsonLive, rowCount: 10) { ThrowAfter = 5 };
        var staging = new FakeStagingWriter();
        var batches = new FakeIngestBatchStore();
        var service = Build(reader, staging, batches, chunkSize: 2);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.IngestAsync(SourceKeys.JsonLive, "key-4", TestContext.Current.CancellationToken));

        Assert.NotNull(batches.Failed);
        Assert.Null(batches.Completed);

        // Rows written before the failure are retained on purpose: a partial batch
        // is reprocessable, whereas discarding it would mean re-fetching upstream.
        Assert.Equal(4, staging.TotalRows);
        Assert.Equal(4, batches.RecordedRows);
    }
}
