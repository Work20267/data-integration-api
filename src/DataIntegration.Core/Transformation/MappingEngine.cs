using System.Globalization;
using DataIntegration.Core.Entities;

namespace DataIntegration.Core.Transformation;

/// <summary>
/// Applies mapping rules to a parsed reading to produce a canonical reading.
/// </summary>
/// <remarks>
/// Deliberately source-agnostic and free of I/O: it receives a
/// <see cref="ParsedReading"/> and a <see cref="MappingRuleSet"/> and returns a
/// result. Everything it knows about a source comes from rule rows, which is
/// what makes "adding a source is configuration" true rather than aspirational.
/// </remarks>
public static class MappingEngine
{
    /// <summary>
    /// Builds the natural key used for idempotent upserts.
    /// </summary>
    /// <remarks>
    /// Truncated to whole seconds on purpose. The live feed and the daily archive
    /// describe the same observations but do not agree on sub-second precision —
    /// the archive publishes <c>2026-10-04T00:00:15</c>. If the key kept
    /// milliseconds, the same reading arriving from both sources would produce two
    /// rows, and the whole idempotency guarantee would quietly fail. See ADR 001.
    /// </remarks>
    public static string BuildExternalKey(int sensorId, DateTime observedAtUtc) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{sensorId}:{observedAtUtc:yyyy-MM-ddTHH:mm:ss}Z");

    public static TransformResult Apply(string sourceKey, ParsedReading reading, MappingRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(rules);

        // Keyed by metric code, because two source fields may legitimately resolve
        // to the same metric and the canonical table permits one value per metric
        // per reading.
        var measurements = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var (field, rawValue) in reading.Values)
        {
            if (!rules.TryResolve(reading.SensorModel, field, out var rule) || rule is null)
            {
                // An unmapped field is not a failure. Sources carry fields we have
                // no canonical metric for, and ignoring them is correct.
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                // Empty is common in the CSV archive (";;" for unused columns) and
                // means "not measured", not "measured as zero".
                continue;
            }

            if (!decimal.TryParse(
                    rawValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                return TransformResult.Failed(
                    TransformErrorCodes.InvalidNumericValue,
                    $"Field '{field}' (metric {rule.MetricCode}) held '{Truncate(rawValue, 80)}', which is not a number.");
            }

            measurements[rule.MetricCode] = value;
        }

        foreach (var required in rules.RequiredFor(reading.SensorModel))
        {
            if (!measurements.ContainsKey(required.MetricCode))
            {
                return TransformResult.Failed(
                    TransformErrorCodes.MissingRequiredField,
                    $"Required field '{required.SourceField}' (metric {required.MetricCode}) was absent or empty.");
            }
        }

        if (measurements.Count == 0)
        {
            return TransformResult.Failed(
                TransformErrorCodes.NoMappedMeasurements,
                $"No field on this {reading.SensorModel} reading resolved to a known metric. "
                + "Most likely a sensor model with no mapping rules yet.");
        }

        var observedAt = TruncateToSecond(reading.ObservedAtUtc);
        var now = DateTime.UtcNow;

        var canonical = new CanonicalReading
        {
            ExternalKey = BuildExternalKey(reading.SensorId, observedAt),
            SensorId = reading.SensorId,
            SensorModel = reading.SensorModel,
            LocationId = reading.LocationId,
            Latitude = reading.Latitude,
            Longitude = reading.Longitude,
            Country = reading.Country,
            IsIndoor = reading.IsIndoor,
            ObservedAtUtc = observedAt,
            LastSourceKey = sourceKey,
            FirstSeenAtUtc = now,
            LastUpdatedAtUtc = now,
            Measurements = [.. measurements.Select(m => new CanonicalMeasurement
            {
                MetricCode = m.Key,
                Value = m.Value,
            })],
        };

        return TransformResult.Success(canonical);
    }

    /// <summary>Matches <see cref="BuildExternalKey"/>, so the key and the stored timestamp agree.</summary>
    private static DateTime TruncateToSecond(DateTime value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), value.Kind);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
