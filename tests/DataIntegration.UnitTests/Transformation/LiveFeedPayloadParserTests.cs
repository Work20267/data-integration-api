using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Sources.SensorCommunity;

namespace DataIntegration.UnitTests.Transformation;

public sealed class LiveFeedPayloadParserTests
{
    private readonly LiveFeedPayloadParser _parser = new();

    /// <summary>
    /// A real element from the live feed, abbreviated. Note the mixed typing that
    /// the parser has to tolerate: ids are JSON numbers, coordinates are JSON
    /// strings, and the timestamp is space-separated rather than ISO-8601.
    /// </summary>
    private const string RealPayload = """
        {
          "id": 30951520978,
          "sampling_rate": null,
          "timestamp": "2026-10-07 06:30:35",
          "location": {
            "id": 88689,
            "latitude": "48.49266638089",
            "longitude": "11.35973332243",
            "altitude": "461.8",
            "country": "DE",
            "exact_location": 1,
            "indoor": 1
          },
          "sensor": {
            "id": 96291,
            "pin": "19",
            "sensor_type": { "id": 36, "name": "SDS011", "manufacturer": "Nova Fitness" }
          },
          "sensordatavalues": [
            { "id": 1, "value": "10.27", "value_type": "P1" },
            { "id": 2, "value": "9.17",  "value_type": "P2" },
            { "id": 3, "value": "65617.50", "value_type": "durP1" }
          ]
        }
        """;

    [Fact]
    public void Parses_a_real_payload()
    {
        var result = _parser.Parse(RealPayload);

        Assert.True(result.Succeeded);

        var reading = result.Reading!;
        Assert.Equal(96291, reading.SensorId);
        Assert.Equal("SDS011", reading.SensorModel);
        Assert.Equal(new DateTime(2026, 10, 7, 6, 30, 35, DateTimeKind.Utc), reading.ObservedAtUtc);
        Assert.Equal(88689, reading.LocationId);
        Assert.Equal("DE", reading.Country);
        Assert.True(reading.IsIndoor);
    }

    [Fact]
    public void Flattens_sensordatavalues_into_a_field_dictionary()
    {
        // The whole reason the mapping engine can be source-agnostic: a nested
        // key-value array becomes the same flat shape a CSV row produces.
        var reading = _parser.Parse(RealPayload).Reading!;

        Assert.Equal("10.27", reading.Values["P1"]);
        Assert.Equal("9.17", reading.Values["P2"]);
        Assert.Equal("65617.50", reading.Values["durP1"]);
        Assert.Equal(3, reading.Values.Count);
    }

    [Fact]
    public void Reads_coordinates_that_arrive_as_strings()
    {
        var reading = _parser.Parse(RealPayload).Reading!;

        Assert.Equal(48.49266638089m, reading.Latitude);
        Assert.Equal(11.35973332243m, reading.Longitude);
    }

    [Fact]
    public void Tolerates_a_numeric_sensor_id_and_an_iso_timestamp()
    {
        // The feed has changed shape before; a readable payload should not be
        // rejected because a field's JSON type or a timestamp's layout differs.
        const string Variant = """
            {
              "timestamp": "2026-10-07T06:30:35Z",
              "sensor": { "id": 42, "sensor_type": { "name": "BME280" } },
              "sensordatavalues": [ { "value": "1013.25", "value_type": "pressure" } ]
            }
            """;

        var result = _parser.Parse(Variant);

        Assert.True(result.Succeeded);
        Assert.Equal(42, result.Reading!.SensorId);
        Assert.Equal(new DateTime(2026, 10, 7, 6, 30, 35, DateTimeKind.Utc), result.Reading.ObservedAtUtc);
    }

    [Fact]
    public void Missing_location_is_not_a_failure()
    {
        const string NoLocation = """
            {
              "timestamp": "2026-10-07 06:30:35",
              "sensor": { "id": 7, "sensor_type": { "name": "DHT22" } },
              "sensordatavalues": [ { "value": "21.5", "value_type": "temperature" } ]
            }
            """;

        var result = _parser.Parse(NoLocation);

        Assert.True(result.Succeeded);
        Assert.Null(result.Reading!.LocationId);
        Assert.Null(result.Reading.Country);
    }

    [Theory]
    [InlineData("not json at all", TransformErrorCodes.PayloadMalformed)]
    [InlineData("[1,2,3]", TransformErrorCodes.PayloadMalformed)]
    [InlineData("""{"timestamp":"2026-10-07 06:30:35"}""", TransformErrorCodes.MissingSensorId)]
    [InlineData("""{"timestamp":"2026-10-07 06:30:35","sensor":{"id":1}}""", TransformErrorCodes.MissingSensorModel)]
    [InlineData("""{"sensor":{"id":1,"sensor_type":{"name":"SDS011"}}}""", TransformErrorCodes.MissingTimestamp)]
    [InlineData("""{"timestamp":"never","sensor":{"id":1,"sensor_type":{"name":"SDS011"}}}""", TransformErrorCodes.InvalidTimestamp)]
    public void Bad_payloads_return_a_specific_error_code_and_never_throw(
        string payload, string expectedCode)
    {
        // Never throwing is the contract. At ~17,700 rows per batch, an exception
        // per bad row would be both slow and the wrong signal — these are expected
        // outcomes that get dead-lettered while the batch carries on.
        var result = _parser.Parse(payload);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Failure!.ErrorCode);
    }

    [Fact]
    public void A_duplicated_value_type_keeps_the_last_occurrence()
    {
        const string Duplicated = """
            {
              "timestamp": "2026-10-07 06:30:35",
              "sensor": { "id": 1, "sensor_type": { "name": "SDS011" } },
              "sensordatavalues": [
                { "value": "1.0", "value_type": "P1" },
                { "value": "2.0", "value_type": "P1" }
              ]
            }
            """;

        var reading = _parser.Parse(Duplicated).Reading!;

        Assert.Equal("2.0", reading.Values["P1"]);
    }
}
