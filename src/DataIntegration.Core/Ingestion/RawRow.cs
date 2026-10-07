namespace DataIntegration.Core.Ingestion;

/// <summary>
/// One upstream row exactly as the source produced it, before any interpretation.
/// Carrying the payload verbatim is the point: a mapping bug found next week is
/// fixable by reprocessing rather than re-fetching from a volunteer-funded source.
/// </summary>
/// <param name="SourceKey">A value from <see cref="SourceKeys"/>.</param>
/// <param name="SensorModel">
/// The model as the source reported it, used to select mapping rules in Phase 4.
/// Null when the source does not say.
/// </param>
/// <param name="Payload">
/// The raw row: one JSON object, or one CSV line. Never reformatted.
/// </param>
public sealed record RawRow(string SourceKey, string? SensorModel, string Payload);
