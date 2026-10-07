namespace DataIntegration.Core.Entities;

/// <summary>
/// A single measured value on a reading. Narrow rather than wide: sensor models
/// report different fields, and a new model must not require a schema migration.
/// </summary>
public class CanonicalMeasurement
{
    public long Id { get; set; }

    public long ReadingId { get; set; }
    public CanonicalReading? Reading { get; set; }

    /// <summary>A value from <see cref="MetricCodes"/>.</summary>
    public string MetricCode { get; set; } = string.Empty;

    public decimal Value { get; set; }
}
