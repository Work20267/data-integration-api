namespace DataIntegration.Core;

/// <summary>
/// Identifies an upstream source. Mapping rules, watermarks and dead letters are
/// all keyed by these values, so they are stable strings rather than an enum —
/// adding a source must not require a code change.
/// </summary>
public static class SourceKeys
{
    /// <summary>Sensor.Community live feed — nested JSON, last five minutes of readings.</summary>
    public const string JsonLive = "json-live";

    /// <summary>Sensor.Community daily archive — semicolon-delimited CSV, one file per sensor.</summary>
    public const string CsvArchive = "csv-archive";
}
