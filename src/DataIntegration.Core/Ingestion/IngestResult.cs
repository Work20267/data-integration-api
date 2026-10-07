namespace DataIntegration.Core.Ingestion;

/// <summary>Outcome of an ingest request.</summary>
public sealed record IngestResult
{
    private IngestResult() { }

    public required Guid BatchId { get; init; }
    public required string SourceKey { get; init; }
    public required int RowsStaged { get; init; }

    /// <summary>
    /// True when this idempotency key had already been processed. The caller gets
    /// the original batch's identity back, and nothing was ingested a second time.
    /// </summary>
    public required bool WasAlreadyProcessed { get; init; }

    public TimeSpan Duration { get; init; }

    public static IngestResult Completed(
        Guid batchId, string sourceKey, int rowsStaged, TimeSpan duration) => new()
        {
            BatchId = batchId,
            SourceKey = sourceKey,
            RowsStaged = rowsStaged,
            WasAlreadyProcessed = false,
            Duration = duration,
        };

    public static IngestResult AlreadyProcessed(
        Guid batchId, string sourceKey, int rowsStaged) => new()
        {
            BatchId = batchId,
            SourceKey = sourceKey,
            RowsStaged = rowsStaged,
            WasAlreadyProcessed = true,
        };
}
