using System.Globalization;
using DataIntegration.Core;
using DataIntegration.Core.Transformation;

namespace DataIntegration.Infrastructure.Sources.SensorCommunity;

/// <summary>
/// Parses one row of a Sensor.Community daily archive CSV.
/// </summary>
/// <remarks>
/// <para>
/// Each payload is two lines — the file's header followed by one data row — so a
/// staged row is self-describing and replayable without the file it came from.
/// </para>
/// <para>
/// The parser knows <b>no sensor model</b>. Column layouts differ per device
/// (<c>sds011</c> carries P1/durP1/ratioP1/P2…, <c>bme280</c> carries
/// pressure/altitude/pressure_sealevel/temperature/humidity), and rather than
/// enumerate them it reads the header and treats every column after the fixed
/// prefix as a candidate field. A device nobody has seen parses fine; whether its
/// fields mean anything is then a question for the mapping rules.
/// </para>
/// </remarks>
public class CsvArchivePayloadParser : IPayloadParser
{
    private const char Delimiter = ';';

    /// <summary>
    /// Columns carrying identity and position rather than measurements. Everything
    /// else in the header is offered to the mapping rules.
    /// </summary>
    private static readonly HashSet<string> MetadataColumns = new(StringComparer.Ordinal)
    {
        "sensor_id", "sensor_type", "location", "lat", "lon", "timestamp",
    };

    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
    ];

    public string SourceKey => SourceKeys.CsvArchive;

    public ParseResult Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return ParseResult.Failed(
                TransformErrorCodes.PayloadMalformed, "Payload was empty.");
        }

        var lines = payload.Split('\n', StringSplitOptions.TrimEntries);

        if (lines.Length < 2)
        {
            return ParseResult.Failed(
                TransformErrorCodes.PayloadMalformed,
                "Expected a header line followed by one data line.");
        }

        var header = lines[0].Split(Delimiter);
        var fields = lines[1].Split(Delimiter);

        // A short row is the common archive defect: a sensor that stopped
        // mid-write. Padding rather than rejecting means the columns that did
        // arrive are still usable, and the missing ones read as "not measured".
        if (fields.Length > header.Length)
        {
            return ParseResult.Failed(
                TransformErrorCodes.PayloadMalformed,
                $"Row has {fields.Length} values but the header declares {header.Length} columns.");
        }

        var byColumn = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i];

            if (name.Length == 0)
            {
                continue;
            }

            byColumn[name] = i < fields.Length ? fields[i] : string.Empty;
        }

        if (!TryReadInt(byColumn, "sensor_id", out var sensorId))
        {
            return ParseResult.Failed(
                TransformErrorCodes.MissingSensorId,
                "Column 'sensor_id' was absent or not an integer.");
        }

        if (!byColumn.TryGetValue("sensor_type", out var model) || model.Length == 0)
        {
            return ParseResult.Failed(
                TransformErrorCodes.MissingSensorModel,
                "Column 'sensor_type' was absent or empty.");
        }

        if (!byColumn.TryGetValue("timestamp", out var rawTimestamp) || rawTimestamp.Length == 0)
        {
            return ParseResult.Failed(
                TransformErrorCodes.MissingTimestamp, "Column 'timestamp' was absent or empty.");
        }

        if (!TryParseTimestamp(rawTimestamp, out var observedAtUtc))
        {
            return ParseResult.Failed(
                TransformErrorCodes.InvalidTimestamp,
                $"timestamp '{rawTimestamp}' is not a recognised date-time.");
        }

        var values = byColumn
            .Where(kv => !MetadataColumns.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        return ParseResult.Success(new ParsedReading
        {
            SensorId = sensorId,
            SensorModel = model,
            ObservedAtUtc = observedAtUtc,
            LocationId = TryReadInt(byColumn, "location", out var location) ? location : null,
            Latitude = TryReadDecimal(byColumn, "lat", out var lat) ? lat : null,
            Longitude = TryReadDecimal(byColumn, "lon", out var lon) ? lon : null,

            // The archive carries neither country nor the indoor flag; the live
            // feed does. Nulls here mean "this source does not say", and the
            // upsert leaves whatever the other source already supplied.
            Country = null,
            IsIndoor = null,

            Values = values,
        });
    }

    private static bool TryParseTimestamp(string raw, out DateTime utc)
    {
        const DateTimeStyles Styles =
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

        return DateTime.TryParseExact(
                   raw, TimestampFormats, CultureInfo.InvariantCulture, Styles, out utc)
               || DateTime.TryParse(raw, CultureInfo.InvariantCulture, Styles, out utc);
    }

    private static bool TryReadInt(Dictionary<string, string> row, string column, out int value)
    {
        value = 0;

        return row.TryGetValue(column, out var raw)
               && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadDecimal(
        Dictionary<string, string> row, string column, out decimal value)
    {
        value = 0;

        return row.TryGetValue(column, out var raw)
               && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
