namespace DataIntegration.Core.Transformation;

/// <summary>
/// A raw payload after the source's format has been dealt with, but before any
/// meaning has been assigned to its field names.
/// </summary>
/// <remarks>
/// This is the seam that makes the mapping layer source-agnostic. A parser knows
/// the <em>format</em> — nested JSON, semicolon-delimited CSV — and nothing else.
/// The mapping engine knows the <em>semantics</em> and never sees a format. Adding
/// a third source is a new parser plus rows in SourceMapping; the engine is
/// untouched.
/// </remarks>
public sealed record ParsedReading
{
    public required int SensorId { get; init; }
    public required string SensorModel { get; init; }
    public required DateTime ObservedAtUtc { get; init; }

    public int? LocationId { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public string? Country { get; init; }
    public bool? IsIndoor { get; init; }

    /// <summary>
    /// Field name exactly as the source spelled it, mapped to its unparsed value.
    /// Values stay as strings here: coercion is the engine's job, and a value that
    /// will not coerce has to be reportable as a specific error rather than
    /// throwing inside a parser.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Values { get; init; }
}
