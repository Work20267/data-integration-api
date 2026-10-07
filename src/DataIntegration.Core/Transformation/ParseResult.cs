namespace DataIntegration.Core.Transformation;

/// <summary>
/// Either a parsed reading or the reason it could not be parsed.
/// </summary>
/// <remarks>
/// A result type rather than exceptions: a malformed row is an expected outcome
/// here, not an exceptional one. At ~17,700 rows per batch, throwing per bad row
/// would be both slow and the wrong signal — these rows are dead-lettered and
/// the batch carries on.
/// </remarks>
public sealed record ParseResult
{
    private ParseResult() { }

    public ParsedReading? Reading { get; private init; }
    public TransformFailure? Failure { get; private init; }

    public bool Succeeded => Reading is not null;

    public static ParseResult Success(ParsedReading reading) =>
        new() { Reading = reading };

    public static ParseResult Failed(string errorCode, string message) =>
        new() { Failure = new TransformFailure(errorCode, message) };
}
