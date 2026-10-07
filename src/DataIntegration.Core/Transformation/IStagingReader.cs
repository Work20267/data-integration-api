using DataIntegration.Core.Entities;

namespace DataIntegration.Core.Transformation;

/// <param name="RawPayload">The payload exactly as ingestion stored it.</param>
public sealed record StagedRow(long Id, string SourceKey, string? SensorModel, string RawPayload);

public interface IStagingReader
{
    /// <summary>
    /// Reads one page of pending rows with an id greater than <paramref name="afterId"/>.
    /// </summary>
    /// <remarks>
    /// Paged rather than streamed, deliberately. A streaming reader holds an open
    /// DataReader on the connection, and the canonical writer needs that same
    /// connection for its bulk copy and MERGE — SQL Server rejects a second
    /// command while a reader is open. Paging closes each read before any write.
    ///
    /// Keyset pagination on the id, not OFFSET: rows are marked only after a
    /// chunk is flushed, so an offset-based page would re-read rows that are still
    /// pending and loop forever.
    /// </remarks>
    Task<IReadOnlyList<StagedRow>> ReadPendingPageAsync(
        Guid batchId,
        long afterId,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks rows with their outcome, so a re-run of the same batch skips what
    /// already succeeded instead of redoing it.
    /// </summary>
    Task MarkAsync(
        IReadOnlyList<long> ids,
        StagingStatus status,
        CancellationToken cancellationToken);
}
