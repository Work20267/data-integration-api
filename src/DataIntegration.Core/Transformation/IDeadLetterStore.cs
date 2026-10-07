using DataIntegration.Core.Entities;

namespace DataIntegration.Core.Transformation;

/// <param name="RawRow">The offending payload verbatim, so a replay needs nothing else.</param>
public sealed record DeadLetterEntry(
    string SourceKey,
    string RawRow,
    string ErrorCode,
    string ErrorMessage);

/// <summary>
/// Stores rows that could not be transformed, per row rather than per batch.
/// </summary>
/// <remarks>
/// One malformed row out of ~17,700 must not reject the other 17,769. The whole
/// point of this store is that a batch has a partial outcome rather than a
/// binary one.
/// </remarks>
public interface IDeadLetterStore
{
    Task WriteAsync(
        Guid batchId,
        IReadOnlyList<DeadLetterEntry> entries,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DeadLetterRecord>> ListAsync(
        DeadLetterStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<DeadLetterRecord?> GetAsync(long id, CancellationToken cancellationToken);

    Task MarkReplayedAsync(long id, CancellationToken cancellationToken);

    /// <summary>Counts grouped by error code — what a dashboard and an operator both want first.</summary>
    Task<IReadOnlyDictionary<string, int>> CountByErrorCodeAsync(
        DeadLetterStatus? status,
        CancellationToken cancellationToken);
}
