using DataIntegration.Core.Entities;

namespace DataIntegration.Core.Transformation;

/// <summary>
/// Persists canonical readings.
/// </summary>
/// <remarks>
/// Upsert, never insert. The live feed and the daily archive carry the same
/// observations, so re-ingesting has to update in place rather than duplicate.
/// Implemented with Dapper and a <c>MERGE</c> against the <c>ExternalKey</c>
/// unique index — EF cannot express this efficiently. See ADR 002.
/// </remarks>
public interface ICanonicalWriter
{
    /// <summary>Returns the number of readings written, new and updated together.</summary>
    Task<int> UpsertAsync(
        IReadOnlyList<CanonicalReading> readings,
        CancellationToken cancellationToken);
}
