using DataIntegration.Core.Entities;
using DataIntegration.Core.Ingestion;
using DataIntegration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DataIntegration.Infrastructure.Ingestion;

/// <summary>
/// Batch bookkeeping through EF Core — low volume and relational, which is the
/// EF side of the split in ADR 002.
/// </summary>
public class IngestBatchStore(DataIntegrationDbContext context) : IIngestBatchStore
{
    public Task<IngestBatch?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        context.IngestBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<IngestBatch> StartAsync(
        string sourceKey,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var batch = new IngestBatch
        {
            Id = Guid.CreateVersion7(),
            SourceKey = sourceKey,
            IdempotencyKey = idempotencyKey,
            StartedAtUtc = DateTime.UtcNow,
            Status = BatchStatus.Running,
        };

        context.IngestBatches.Add(batch);
        await context.SaveChangesAsync(cancellationToken);

        return batch;
    }

    public Task CompleteAsync(Guid batchId, int rowsReceived, CancellationToken cancellationToken) =>
        FinishAsync(batchId, rowsReceived, BatchStatus.Completed, cancellationToken);

    public Task FailAsync(Guid batchId, int rowsReceived, CancellationToken cancellationToken) =>
        FinishAsync(batchId, rowsReceived, BatchStatus.Failed, cancellationToken);

    private async Task FinishAsync(
        Guid batchId,
        int rowsReceived,
        BatchStatus status,
        CancellationToken cancellationToken)
    {
        // ExecuteUpdate issues a single UPDATE rather than loading the entity to
        // change two columns.
        await context.IngestBatches
            .Where(b => b.Id == batchId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(b => b.Status, status)
                    .SetProperty(b => b.RowsReceived, rowsReceived)
                    .SetProperty(b => b.CompletedAtUtc, DateTime.UtcNow),
                cancellationToken);
    }
}
