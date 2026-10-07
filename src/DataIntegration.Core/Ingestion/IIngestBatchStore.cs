using DataIntegration.Core.Entities;

namespace DataIntegration.Core.Ingestion;

/// <summary>
/// Batch bookkeeping. Low volume and relational, so this is the EF Core side of
/// the split described in ADR 002.
/// </summary>
public interface IIngestBatchStore
{
    /// <summary>
    /// Returns the existing batch for this idempotency key, or null. A replayed
    /// request must be recognised and reported, never re-processed.
    /// </summary>
    Task<IngestBatch?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<IngestBatch> StartAsync(
        string sourceKey,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid batchId,
        int rowsReceived,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks the batch failed. The rows already written to staging are kept on
    /// purpose: a partial batch is reprocessable, whereas a deleted one is lost.
    /// </summary>
    Task FailAsync(
        Guid batchId,
        int rowsReceived,
        CancellationToken cancellationToken);
}
