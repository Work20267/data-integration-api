namespace DataIntegration.Core;

/// <summary>
/// The canonical measurement vocabulary. Upstream field names vary per source and
/// per sensor model; every one of them resolves to exactly one of these codes.
/// </summary>
public static class MetricCodes
{
    // Particulate matter. Note the upstream trap: in the Sensor.Community schema
    // P1 is PM10 and P2 is PM2.5 — P1 is NOT PM1 (that is P0). Mapping those by
    // name rather than by meaning is silently wrong.
    public const string Pm1 = "PM1";
    public const string Pm25 = "PM2_5";
    public const string Pm4 = "PM4";
    public const string Pm10 = "PM10";

    public const string Temperature = "TEMPERATURE";
    public const string Humidity = "HUMIDITY";
    public const string Pressure = "PRESSURE";
    public const string PressureSeaLevel = "PRESSURE_SEALEVEL";
    public const string Altitude = "ALTITUDE";
    public const string NoiseLaeq = "NOISE_LAEQ";

    /// <summary>
    /// Counts per minute from a Geiger-Müller tube. Added after a real batch
    /// dead-lettered 63 rows from a "Radiation Si22G" sensor nobody had written
    /// rules for — the network carries more than air quality.
    /// </summary>
    public const string RadiationCpm = "RADIATION_CPM";
}
