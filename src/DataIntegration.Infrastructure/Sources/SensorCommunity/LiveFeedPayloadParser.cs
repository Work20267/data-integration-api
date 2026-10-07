using System.Globalization;
using System.Text.Json;
using DataIntegration.Core;
using DataIntegration.Core.Transformation;

namespace DataIntegration.Infrastructure.Sources.SensorCommunity;

/// <summary>
/// Parses one element of the Sensor.Community live JSON feed.
/// </summary>
/// <remarks>
/// Knows the format and nothing about meaning: it flattens the
/// <c>sensordatavalues</c> array of <c>{value_type, value}</c> pairs into a plain
/// field dictionary and leaves every value as a string. Which field is PM10 and
/// which is PM2.5 is decided by mapping rules, not here.
///
/// The feed is produced by hobbyist firmware and is not consistently typed — ids
/// arrive as JSON numbers, coordinates as JSON strings — so every read tolerates
/// either.
/// </remarks>
public class LiveFeedPayloadParser : IPayloadParser
{
    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "yyyy-MM-ddTHH:mm:ssK",
    ];

    public string SourceKey => SourceKeys.JsonLive;

    public ParseResult Parse(string payload)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            return ParseResult.Failed(
                TransformErrorCodes.PayloadMalformed,
                $"Payload is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return ParseResult.Failed(
                    TransformErrorCodes.PayloadMalformed,
                    $"Expected a JSON object, found {root.ValueKind}.");
            }

            if (!TryGetObject(root, "sensor", out var sensor)
                || !TryReadInt(sensor, "id", out var sensorId))
            {
                return ParseResult.Failed(
                    TransformErrorCodes.MissingSensorId,
                    "sensor.id was absent or not an integer.");
            }

            var model = ReadSensorModel(sensor);
            if (string.IsNullOrWhiteSpace(model))
            {
                return ParseResult.Failed(
                    TransformErrorCodes.MissingSensorModel,
                    "sensor.sensor_type.name was absent.");
            }

            if (!TryReadString(root, "timestamp", out var rawTimestamp))
            {
                return ParseResult.Failed(
                    TransformErrorCodes.MissingTimestamp,
                    "timestamp was absent.");
            }

            if (!TryParseTimestamp(rawTimestamp, out var observedAtUtc))
            {
                return ParseResult.Failed(
                    TransformErrorCodes.InvalidTimestamp,
                    $"timestamp '{rawTimestamp}' is not a recognised date-time.");
            }

            var values = ReadMeasurementFields(root);
            var reading = BuildReading(root, sensorId, model, observedAtUtc, values);

            return ParseResult.Success(reading);
        }
    }

    private static ParsedReading BuildReading(
        JsonElement root,
        int sensorId,
        string model,
        DateTime observedAtUtc,
        Dictionary<string, string> values)
    {
        int? locationId = null;
        decimal? latitude = null;
        decimal? longitude = null;
        string? country = null;
        bool? indoor = null;

        if (TryGetObject(root, "location", out var location))
        {
            if (TryReadInt(location, "id", out var id))
            {
                locationId = id;
            }

            if (TryReadDecimal(location, "latitude", out var lat))
            {
                latitude = lat;
            }

            if (TryReadDecimal(location, "longitude", out var lon))
            {
                longitude = lon;
            }

            if (TryReadString(location, "country", out var c) && c.Length > 0)
            {
                country = c;
            }

            // "indoor" arrives as 0 or 1, sometimes as "0" or "1".
            if (TryReadInt(location, "indoor", out var indoorFlag))
            {
                indoor = indoorFlag != 0;
            }
        }

        return new ParsedReading
        {
            SensorId = sensorId,
            SensorModel = model,
            ObservedAtUtc = observedAtUtc,
            LocationId = locationId,
            Latitude = latitude,
            Longitude = longitude,
            Country = country,
            IsIndoor = indoor,
            Values = values,
        };
    }

    /// <summary>
    /// Flattens sensordatavalues into field → raw value. A duplicated value_type
    /// keeps the last occurrence, matching the canonical table's one-value-per-
    /// metric constraint.
    /// </summary>
    private static Dictionary<string, string> ReadMeasurementFields(JsonElement root)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!root.TryGetProperty("sensordatavalues", out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryReadString(entry, "value_type", out var type)
                && type.Length > 0
                && TryReadString(entry, "value", out var value))
            {
                values[type] = value;
            }
        }

        return values;
    }

    private static string? ReadSensorModel(JsonElement sensor) =>
        TryGetObject(sensor, "sensor_type", out var sensorType)
        && TryReadString(sensorType, "name", out var name)
            ? name
            : null;

    private static bool TryParseTimestamp(string raw, out DateTime utc)
    {
        const DateTimeStyles Styles =
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

        if (DateTime.TryParseExact(raw, TimestampFormats, CultureInfo.InvariantCulture, Styles, out utc))
        {
            return true;
        }

        // Fallback for any format the list above does not cover; the feed has
        // changed shape before and a readable timestamp should not be rejected
        // just because its layout is new.
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, Styles, out utc);
    }

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement value) =>
        parent.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;

    /// <summary>Reads a property as text whether the source wrote a string or a number.</summary>
    private static bool TryReadString(JsonElement parent, string name, out string value)
    {
        value = string.Empty;

        if (!parent.TryGetProperty(name, out var property))
        {
            return false;
        }

        switch (property.ValueKind)
        {
            case JsonValueKind.String:
                value = property.GetString() ?? string.Empty;
                return true;
            case JsonValueKind.Number:
                value = property.GetRawText();
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadInt(JsonElement parent, string name, out int value)
    {
        value = 0;

        return TryReadString(parent, name, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadDecimal(JsonElement parent, string name, out decimal value)
    {
        value = 0;

        return TryReadString(parent, name, out var raw)
            && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
