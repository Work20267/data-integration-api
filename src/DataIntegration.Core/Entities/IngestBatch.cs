namespace DataIntegration.Core.Entities;

/// <summary>
/// One ingest operation. Carries the caller's idempotency key so a replayed
/// request is recognised rather than re-processed, and the per-batch counts the
/// dead-letter and metrics surfaces report.
/// </summary>
public class IngestBatch
{
    public Guid Id { get; set; }

    public string SourceKey { get; set; } = string.Empty;

    /// <summary>
    /// From the Idempotency-Key header, or derived from the payload when absent.
    /// Unique — a second batch with the same key is rejected, not re-run.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public BatchStatus Status { get; set; }

    public int RowsReceived { get; set; }
    public int RowsCanonical { get; set; }
    public int RowsDeadLettered { get; set; }

    public ICollection<StagingRecord> StagingRecords { get; set; } = [];
}
