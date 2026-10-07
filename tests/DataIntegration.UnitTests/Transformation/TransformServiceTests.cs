using DataIntegration.Core;
using DataIntegration.Core.Entities;
using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Observability;
using DataIntegration.Infrastructure.Transformation;
using DataIntegration.UnitTests.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataIntegration.UnitTests.Transformation;

public sealed class TransformServiceTests
{
    private static readonly Guid BatchId = Guid.CreateVersion7();

    private static MappingRuleSet Rules() =>
        new(SourceKeys.JsonLive,
            [new MappingRule(SourceKeys.JsonLive, null, "P1", MetricCodes.Pm10, false)]);

    private static List<StagedRow> Rows(int count) =>
        [.. Enumerable.Range(1, count).Select(i =>
            new StagedRow(i, SourceKeys.JsonLive, "SDS011", $"payload-{i}"))];

    private static (TransformService Service, FakeCanonicalWriter Canonical,
                    FakeDeadLetterStore DeadLetters, FakeStagingReader Staging)
        Build(IReadOnlyList<StagedRow> rows, ScriptedParser parser, int chunkSize = 100)
    {
        using var meterFactory = new TestMeterFactory();

        var staging = new FakeStagingReader(rows);
        var canonical = new FakeCanonicalWriter();
        var deadLetters = new FakeDeadLetterStore();

        var service = new TransformService(
            [parser],
            new FakeMappingRuleProvider(Rules()),
            staging,
            canonical,
            deadLetters,
            new TransformMetrics(meterFactory),
            Options.Create(new TransformOptions { ChunkSize = chunkSize }),
            NullLogger<TransformService>.Instance);

        return (service, canonical, deadLetters, staging);
    }

    [Fact]
    public async Task One_exploding_row_costs_exactly_one_dead_letter()
    {
        // The guarantee the whole design exists to provide. An unanticipated
        // payload — hobbyist firmware produces them — must not reject the batch.
        var parser = new ScriptedParser();
        parser.ThrowOn.Add("payload-7");

        var (service, canonical, deadLetters, _) = Build(Rows(20), parser);

        var summary = await service.TransformBatchAsync(
            BatchId, TestContext.Current.CancellationToken);

        Assert.Equal(20, summary.RowsRead);
        Assert.Equal(19, summary.RowsCanonical);
        Assert.Equal(1, summary.RowsDeadLettered);

        Assert.Equal(19, canonical.Written.Count);

        var dead = Assert.Single(deadLetters.Written);
        Assert.Equal("payload-7", dead.RawRow);

        // The exception type survives into the message, so the cause is still
        // diagnosable from the dead letter alone.
        Assert.Contains("InvalidOperationException", dead.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cleanly_rejected_row_is_dead_lettered_with_its_own_code()
    {
        var parser = new ScriptedParser();
        parser.FailOn.Add("payload-3");

        var (service, _, deadLetters, _) = Build(Rows(5), parser);

        await service.TransformBatchAsync(BatchId, TestContext.Current.CancellationToken);

        var dead = Assert.Single(deadLetters.Written);
        Assert.Equal(TransformErrorCodes.PayloadMalformed, dead.ErrorCode);
    }

    [Fact]
    public async Task Rows_are_marked_by_outcome_so_a_rerun_skips_what_succeeded()
    {
        var parser = new ScriptedParser();
        parser.FailOn.Add("payload-2");

        var (service, _, _, staging) = Build(Rows(4), parser);

        await service.TransformBatchAsync(BatchId, TestContext.Current.CancellationToken);

        var transformed = staging.Marks.Single(m => m.Status == StagingStatus.Transformed);
        var rejected = staging.Marks.Single(m => m.Status == StagingStatus.Rejected);

        Assert.Equal([1L, 3L, 4L], transformed.Ids);
        Assert.Equal([2L], rejected.Ids);
    }

    [Fact]
    public async Task Pages_through_rows_larger_than_the_chunk_size()
    {
        var (service, canonical, _, _) = Build(Rows(250), new ScriptedParser(), chunkSize: 100);

        var summary = await service.TransformBatchAsync(
            BatchId, TestContext.Current.CancellationToken);

        // Keyset paging must terminate and see every row exactly once — an
        // offset-based page would re-read still-pending rows forever.
        Assert.Equal(250, summary.RowsRead);
        Assert.Equal(250, canonical.Written.Count);
    }

    [Fact]
    public async Task An_empty_batch_completes_without_writing_anything()
    {
        var (service, canonical, deadLetters, _) = Build([], new ScriptedParser());

        var summary = await service.TransformBatchAsync(
            BatchId, TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.RowsRead);
        Assert.Empty(canonical.Written);
        Assert.Empty(deadLetters.Written);
    }
}
