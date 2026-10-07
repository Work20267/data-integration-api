using DataIntegration.Core;
using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Sources.SensorCommunity;

namespace DataIntegration.UnitTests.Transformation;

public sealed class CsvArchivePayloadParserTests
{
    private readonly CsvArchivePayloadParser _parser = new();

    // Real headers and rows from archive.sensor.community/2026-10-04/.
    // Note the two layouts share only the first six columns.
    private const string Sds011Header =
        "sensor_id;sensor_type;location;lat;lon;timestamp;P1;durP1;ratioP1;P2;durP2;ratioP2";

    private const string Sds011Row =
        "1000;SDS011;89630;47.600;13.166;2026-10-04T00:00:15;10.27;;;9.17;;";

    private const string Bme280Header =
        "sensor_id;sensor_type;location;lat;lon;timestamp;pressure;altitude;pressure_sealevel;temperature;humidity";

    private const string Bme280Row =
        "100017;BME280;93323;53.58135997;9.86659706;2026-10-04T00:00:35;102543.13;;;10.30;100.00";

    private static string Payload(string header, string row) => $"{header}\n{row}";

    [Fact]
    public void Parses_an_sds011_row()
    {
        var result = _parser.Parse(Payload(Sds011Header, Sds011Row));

        Assert.True(result.Succeeded);

        var reading = result.Reading!;
        Assert.Equal(1000, reading.SensorId);
        Assert.Equal("SDS011", reading.SensorModel);
        Assert.Equal(89630, reading.LocationId);
        Assert.Equal(47.600m, reading.Latitude);
        Assert.Equal(new DateTime(2026, 10, 4, 0, 0, 15, DateTimeKind.Utc), reading.ObservedAtUtc);
    }

    [Fact]
    public void Parses_a_bme280_row_with_a_completely_different_column_set()
    {
        // The same parser, no model-specific code. This is the point of reading
        // field names from the header rather than enumerating layouts.
        var result = _parser.Parse(Payload(Bme280Header, Bme280Row));

        Assert.True(result.Succeeded);

        var reading = result.Reading!;
        Assert.Equal("BME280", reading.SensorModel);
        Assert.Equal("102543.13", reading.Values["pressure"]);
        Assert.Equal("10.30", reading.Values["temperature"]);
        Assert.Equal("100.00", reading.Values["humidity"]);
    }

    [Fact]
    public void Metadata_columns_are_excluded_from_the_measurement_fields()
    {
        var reading = _parser.Parse(Payload(Sds011Header, Sds011Row)).Reading!;

        // sensor_id, sensor_type, location, lat, lon and timestamp are identity
        // and position, not observations, and must not reach the mapping rules.
        Assert.DoesNotContain("sensor_id", reading.Values.Keys);
        Assert.DoesNotContain("timestamp", reading.Values.Keys);
        Assert.DoesNotContain("lat", reading.Values.Keys);

        // Ordinal ordering puts the uppercase P columns before the lowercase ones.
        Assert.Equal(
            ["P1", "P2", "durP1", "durP2", "ratioP1", "ratioP2"],
            reading.Values.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Empty_columns_survive_as_empty_rather_than_being_dropped()
    {
        // The archive writes ";;" for columns a sensor does not populate. The
        // engine treats empty as "not measured"; it must not become 0.
        var reading = _parser.Parse(Payload(Sds011Header, Sds011Row)).Reading!;

        Assert.Equal(string.Empty, reading.Values["durP1"]);
        Assert.Equal("10.27", reading.Values["P1"]);
    }

    [Fact]
    public void A_truncated_row_keeps_the_columns_that_did_arrive()
    {
        // A sensor that stops mid-write is the common archive defect. Rejecting
        // the row would throw away the readings it did manage to report.
        var truncated = "1000;SDS011;89630;47.600;13.166;2026-10-04T00:00:15;10.27";

        var result = _parser.Parse(Payload(Sds011Header, truncated));

        Assert.True(result.Succeeded);
        Assert.Equal("10.27", result.Reading!.Values["P1"]);
        Assert.Equal(string.Empty, result.Reading.Values["P2"]);
    }

    [Fact]
    public void A_row_with_more_values_than_columns_is_rejected()
    {
        // The opposite case is not recoverable: extra values mean the row cannot
        // be aligned to the header, so every column is suspect.
        var tooLong = Sds011Row + ";surprise;extra";

        var result = _parser.Parse(Payload(Sds011Header, tooLong));

        Assert.False(result.Succeeded);
        Assert.Equal(TransformErrorCodes.PayloadMalformed, result.Failure!.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("only-a-header-line")]
    public void A_payload_without_both_lines_is_malformed(string payload)
    {
        var result = _parser.Parse(payload);

        Assert.False(result.Succeeded);
        Assert.Equal(TransformErrorCodes.PayloadMalformed, result.Failure!.ErrorCode);
    }

    [Fact]
    public void A_non_numeric_sensor_id_is_reported_specifically()
    {
        var bad = "not-a-number;SDS011;1;1;1;2026-10-04T00:00:15;1";

        var result = _parser.Parse(Payload(Sds011Header, bad));

        Assert.False(result.Succeeded);
        Assert.Equal(TransformErrorCodes.MissingSensorId, result.Failure!.ErrorCode);
    }

    [Fact]
    public void Both_sources_produce_the_same_external_key_for_the_same_observation()
    {
        // The guarantee the whole project rests on. The live feed writes
        // "2026-10-04 00:00:15" and the archive writes "2026-10-04T00:00:15" for
        // the same reading; if these produced different keys, ingesting both
        // sources would duplicate every row.
        var fromArchive = _parser.Parse(Payload(Sds011Header, Sds011Row)).Reading!;

        var liveEquivalent = new LiveFeedPayloadParser().Parse("""
            {
              "timestamp": "2026-10-04 00:00:15",
              "sensor": { "id": 1000, "sensor_type": { "name": "SDS011" } },
              "sensordatavalues": [ { "value": "10.27", "value_type": "P1" } ]
            }
            """).Reading!;

        Assert.Equal(
            MappingEngine.BuildExternalKey(liveEquivalent.SensorId, liveEquivalent.ObservedAtUtc),
            MappingEngine.BuildExternalKey(fromArchive.SensorId, fromArchive.ObservedAtUtc));
    }

    [Fact]
    public void Archive_rows_resolve_through_the_archive_mapping_rules()
    {
        // pressure_sealevel here, pressure_at_sealevel in the JSON feed — the
        // disagreement that justifies mapping being data rather than code.
        var reading = _parser.Parse(Payload(Bme280Header, Bme280Row)).Reading!;

        var rules = new MappingRuleSet(SourceKeys.CsvArchive,
        [
            new MappingRule(SourceKeys.CsvArchive, null, "pressure", MetricCodes.Pressure, false),
            new MappingRule(SourceKeys.CsvArchive, null, "pressure_sealevel", MetricCodes.PressureSeaLevel, false),
            new MappingRule(SourceKeys.CsvArchive, null, "temperature", MetricCodes.Temperature, false),
            new MappingRule(SourceKeys.CsvArchive, null, "humidity", MetricCodes.Humidity, false),
        ]);

        var result = MappingEngine.Apply(SourceKeys.CsvArchive, reading, rules);

        Assert.True(result.Succeeded);

        var metrics = result.Reading!.Measurements.Select(m => m.MetricCode).Order().ToList();

        // pressure_sealevel was empty in this row, so three of four resolve.
        Assert.Equal([MetricCodes.Humidity, MetricCodes.Pressure, MetricCodes.Temperature], metrics);
    }
}
