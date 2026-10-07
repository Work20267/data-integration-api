namespace DataIntegration.Core.Transformation;

/// <summary>
/// Stable, machine-readable failure reasons. Written to
/// <c>DeadLetterRecord.ErrorCode</c>, which is indexed, so a dashboard can group
/// by cause and an operator can see whether 400 failures are one problem or four.
/// </summary>
public static class TransformErrorCodes
{
    /// <summary>No parser is registered for the row's source.</summary>
    public const string NoParserForSource = "NO_PARSER_FOR_SOURCE";

    /// <summary>The payload is not well-formed for its declared format.</summary>
    public const string PayloadMalformed = "PAYLOAD_MALFORMED";

    public const string MissingSensorId = "MISSING_SENSOR_ID";
    public const string MissingSensorModel = "MISSING_SENSOR_MODEL";
    public const string MissingTimestamp = "MISSING_TIMESTAMP";
    public const string InvalidTimestamp = "INVALID_TIMESTAMP";

    /// <summary>A mapped field held something that is not a number.</summary>
    public const string InvalidNumericValue = "INVALID_NUMERIC_VALUE";

    /// <summary>A mapping rule marked required had no value in the payload.</summary>
    public const string MissingRequiredField = "MISSING_REQUIRED_FIELD";

    /// <summary>
    /// Parsed cleanly but nothing in it maps to a known metric. Usually a new
    /// sensor model whose fields have no rules yet — which is a configuration gap,
    /// not corrupt data, and is exactly what the dead letter is for.
    /// </summary>
    public const string NoMappedMeasurements = "NO_MAPPED_MEASUREMENTS";
}
