namespace DataIntegration.Core.Ingestion;

/// <summary>
/// Persists raw rows into staging. Deliberately not an EF repository: this is the
/// bulk path, and the implementation uses SqlBulkCopy. See ADR 002.
/// </summary>
public interface IStagingWriter
{
    /// <summary>
    /// Writes one chunk and returns the number of rows persisted. Called
    /// repeatedly as rows stream in, so each call is its own round trip.
    /// </summary>
    Task<int> WriteChunkAsync(
        Guid batchId,
        IReadOnlyList<RawRow> rows,
        CancellationToken cancellationToken);
}
