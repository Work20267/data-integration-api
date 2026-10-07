namespace DataIntegration.Core.Entities;

/// <summary>
/// One observation event in canonical form, independent of which source produced it.
/// </summary>
public class CanonicalReading
{
    public long Id { get; set; }

    /// <summary>
    /// Natural key: "{sensorId}:{observedAtUtc:O}". The live feed and the daily
    /// archive carry the same readings, so this is what makes ingesting both
    /// idempotent instead of duplicating every row.
    /// </summary>
    public string ExternalKey { get; set; } = string.Empty;

    public int SensorId { get; set; }
    public string SensorModel { get; set; } = string.Empty;

    public int? LocationId { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? Country { get; set; }
    public bool? IsIndoor { get; set; }

    public DateTime ObservedAtUtc { get; set; }

    /// <summary>Which source last wrote this row — the archive may overwrite the live feed.</summary>
    public string LastSourceKey { get; set; } = string.Empty;

    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastUpdatedAtUtc { get; set; }

    public ICollection<CanonicalMeasurement> Measurements { get; set; } = [];
}
