namespace DataIntegration.Core.Transformation;

/// <summary>
/// Turns one source's raw payload into a <see cref="ParsedReading"/>.
/// </summary>
/// <remarks>
/// Implementations know a format and nothing else — no mapping, no metric codes,
/// no canonical model. That restriction is what keeps the mapping engine shared
/// across sources whose shapes have nothing in common.
/// </remarks>
public interface IPayloadParser
{
    /// <summary>A value from <see cref="SourceKeys"/>.</summary>
    string SourceKey { get; }

    /// <summary>
    /// Never throws for bad input. A malformed payload is returned as a failure so
    /// the caller can dead-letter the row and continue with the rest of the batch.
    /// </summary>
    ParseResult Parse(string payload);
}
