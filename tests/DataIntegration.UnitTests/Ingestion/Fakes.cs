using System.Diagnostics.Metrics;
using DataIntegration.Core.Entities;
using DataIntegration.Core.Ingestion;

namespace DataIntegration.UnitTests.Ingestion;

/// <summary>
/// Hand-written fakes rather than a mocking library: there are three small
/// interfaces, and recording calls directly makes the chunking assertions far
/// easier to read than configured mock expectations.
/// </summary>
internal sealed class FakeUpstreamReader(string sourceKey, int rowCount) : IUpstreamReader
{
    public string SourceKey { get; } = sourceKey;

    public bool WasRead { get; private set; }

    /// <summary>When set, throws after yielding this many rows.</summary>
    public int? ThrowAfter { get; init; }

    public async IAsyncEnumerable<RawRow> ReadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        WasRead = true;

        for (var i = 0; i < rowCount; i++)
        {
            if (ThrowAfter is int limit && i == limit)
            {
                throw new HttpRequestException("upstream went away");
            }

            await Task.Yield();
            yield return new RawRow(SourceKey, "SDS011", $"{{\"n\":{i}}}");
        }
    }
}

internal sealed class FakeStagingWriter : IStagingWriter
{
    /// <summary>Row count of each chunk, in order — this is what the chunking tests assert on.</summary>
    public List<int> ChunkSizes { get; } = [];

    public int TotalRows => ChunkSizes.Sum();

    public Task<int> WriteChunkAsync(
        Guid batchId,
        IReadOnlyList<RawRow> rows,
        CancellationToken cancellationToken)
    {
        ChunkSizes.Add(rows.Count);
        return Task.FromResult(rows.Count);
    }
}

internal sealed class FakeIngestBatchStore : IIngestBatchStore
{
    private readonly Dictionary<string, IngestBatch> _byKey = [];

    public IngestBatch? Completed { get; private set; }
    public IngestBatch? Failed { get; private set; }
    public int RecordedRows { get; private set; }

    public void SeedExisting(IngestBatch batch) => _byKey[batch.IdempotencyKey] = batch;

    public Task<IngestBatch?> FindByIdempotencyKeyAsync(
        string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(_byKey.GetValueOrDefault(idempotencyKey));

    public Task<IngestBatch> StartAsync(
        string sourceKey, string idempotencyKey, CancellationToken cancellationToken)
    {
        var batch = new IngestBatch
        {
            Id = Guid.CreateVersion7(),
            SourceKey = sourceKey,
            IdempotencyKey = idempotencyKey,
            StartedAtUtc = DateTime.UtcNow,
            Status = BatchStatus.Running,
        };

        _byKey[idempotencyKey] = batch;
        return Task.FromResult(batch);
    }

    public Task CompleteAsync(Guid batchId, int rowsReceived, CancellationToken cancellationToken)
    {
        Completed = _byKey.Values.First(b => b.Id == batchId);
        RecordedRows = rowsReceived;
        return Task.CompletedTask;
    }

    public Task FailAsync(Guid batchId, int rowsReceived, CancellationToken cancellationToken)
    {
        Failed = _byKey.Values.First(b => b.Id == batchId);
        RecordedRows = rowsReceived;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Minimal IMeterFactory so tests need no DI container. Each test gets its own
/// meters, so instrument state cannot leak between them.
/// </summary>
internal sealed class TestMeterFactory : IMeterFactory
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
