using DataIntegration.Core;
using DataIntegration.Core.Transformation;

namespace DataIntegration.UnitTests.Transformation;

public sealed class MappingEngineTests
{
    private static readonly DateTime Observed = new(2026, 10, 7, 6, 30, 35, DateTimeKind.Utc);

    private static MappingRuleSet Rules(params MappingRule[] rules) =>
        new(SourceKeys.JsonLive, rules);

    private static MappingRule Rule(
        string field, string metric, string? model = null, bool required = false) =>
        new(SourceKeys.JsonLive, model, field, metric, required);

    private static ParsedReading Reading(
        params (string Field, string Value)[] values) =>
        new()
        {
            SensorId = 1000,
            SensorModel = "SDS011",
            ObservedAtUtc = Observed,
            Values = values.ToDictionary(v => v.Field, v => v.Value, StringComparer.Ordinal),
        };

    [Fact]
    public void Maps_P1_to_PM10_and_P2_to_PM2_5()
    {
        // The upstream trap: P1 is PM10 and P2 is PM2.5. P1 is NOT PM1 — that is
        // P0. Mapping by name rather than by meaning is silently wrong, so this
        // test exists specifically to stop someone "fixing" it. See ADR 001.
        var result = MappingEngine.Apply(
            SourceKeys.JsonLive,
            Reading(("P1", "10.27"), ("P2", "9.17")),
            Rules(Rule("P1", MetricCodes.Pm10), Rule("P2", MetricCodes.Pm25)));

        Assert.True(result.Succeeded);

        var byMetric = result.Reading!.Measurements.ToDictionary(m => m.MetricCode, m => m.Value);

        Assert.Equal(10.27m, byMetric[MetricCodes.Pm10]);
        Assert.Equal(9.17m, byMetric[MetricCodes.Pm25]);
        Assert.DoesNotContain(MetricCodes.Pm1, byMetric.Keys);
    }

    [Fact]
    public void A_model_specific_rule_wins_over_a_shared_one()
    {
        var result = MappingEngine.Apply(
            SourceKeys.JsonLive,
            Reading(("value", "42")),
            Rules(
                Rule("value", MetricCodes.Temperature),
                Rule("value", MetricCodes.Humidity, model: "SDS011")));

        Assert.True(result.Succeeded);
        Assert.Equal(
            MetricCodes.Humidity,
            Assert.Single(result.Reading!.Measurements).MetricCode);
    }

    [Fact]
    public void An_unmapped_field_is_ignored_rather_than_failing_the_row()
    {
        // Sources carry fields we have no canonical metric for — durP1, ratioP1,
        // sample_time_ms. Ignoring them is correct, not a data-quality problem.
        var result = MappingEngine.Apply(
            SourceKeys.JsonLive,
            Reading(("P1", "10.27"), ("durP1", "65617.50"), ("ratioP1", "0.22")),
            Rules(Rule("P1", MetricCodes.Pm10)));

        Assert.True(result.Succeeded);
        Assert.Single(result.Reading!.Measurements);
    }

    [Fact]
    public void An_empty_value_means_not_measured_not_measured_as_zero()
    {
        // The CSV archive writes ";;" for columns a sensor does not populate.
        // Recording those as 0 would invent data that was never collected.
        var result = MappingEngine.Apply(
            SourceKeys.JsonLive,
            Reading(("P1", "10.27"), ("P2", "")),
            Rules(Rule("P1", MetricCodes.Pm10), Rule("P2", MetricCodes.Pm25)));

        Assert.True(result.Succeeded);

        var single = Assert.Single(result.Reading!.Measurements);
        Assert.Equal(MetricCodes.Pm10, single.MetricCode);
    }

    [Fact]
    public void A_non_numeric_value_in_a_mapped_field_fails_the_row_with_its_code()
    {
        var result = MappingEngine.Apply(
            SourceKeys.JsonLive,
            Reading(("P1", "not-a-number")),
            Rules(Rule("P1", MetricCodes.Pm10)));

        Assert.False(result.Succeeded);
        Assert.Equal(TransformErrorCodes.InvalidNumericValue, result.Failure!.ErrorCode);

        // The offending value belongs in the message, so a dead letter is
        // diagnosable without going back to the raw payload.
        Assert.Contains("not-a-number", result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reading_whose_fields_map_to_nothing_is_reported_as_a_config_gap()
    {
        // This is the real Radiation Si22G case: a sensor model nobody has written
        // rules for yet. A configuration gap, not corrupt data — which is why it
        // is dead-lettered and replayable rather than discarded.
        var result = MappingEngine.Apply(
            SourceKeys.JsonLive,
            Reading(("counts_per_minute", "104.00"), ("hv_pulses", "73.50")),
            Rules(Rule("P1", MetricCodes.Pm10)));

        Assert.False(result.Succeeded);
        Assert.Equal(TransformErrorCodes.NoMappedMeasurements, result.Failure!.ErrorCode);
    }

    [Fact]
    public void A_missing_required_field_fails_the_row()
    {
        var result = MappingEngine.Apply(
            SourceKeys.JsonLive,
            Reading(("P2", "9.17")),
            Rules(
                Rule("P1", MetricCodes.Pm10, required: true),
                Rule("P2", MetricCodes.Pm25)));

        Assert.False(result.Succeeded);
        Assert.Equal(TransformErrorCodes.MissingRequiredField, result.Failure!.ErrorCode);
    }

    [Fact]
    public void Two_fields_mapping_to_the_same_metric_collapse_to_one_measurement()
    {
        // The canonical table permits one value per metric per reading, so the
        // engine must collapse rather than emit a pair that violates the index.
        var result = MappingEngine.Apply(
            SourceKeys.JsonLive,
            Reading(("pressure_at_sealevel", "102543.13"), ("pressure_sealevel", "102543.13")),
            Rules(
                Rule("pressure_at_sealevel", MetricCodes.PressureSeaLevel),
                Rule("pressure_sealevel", MetricCodes.PressureSeaLevel)));

        Assert.True(result.Succeeded);
        Assert.Single(result.Reading!.Measurements);
    }

    [Fact]
    public void The_external_key_is_truncated_to_whole_seconds()
    {
        // The single most important property for idempotency. The live feed and the
        // daily archive describe the same observations but disagree on sub-second
        // precision; if the key kept milliseconds the same reading would store
        // twice and the guarantee would fail silently. See ADR 001.
        var withMillis = new DateTime(2026, 10, 7, 6, 30, 35, 847, DateTimeKind.Utc);

        var a = MappingEngine.BuildExternalKey(1000, withMillis);
        var b = MappingEngine.BuildExternalKey(1000, Observed);

        Assert.Equal(b, a);
        Assert.Equal("1000:2026-10-07T06:30:35Z", a);
    }

    [Fact]
    public void The_stored_timestamp_agrees_with_the_external_key()
    {
        var withMillis = new DateTime(2026, 10, 7, 6, 30, 35, 847, DateTimeKind.Utc);

        var result = MappingEngine.Apply(
            SourceKeys.JsonLive,
            Reading(("P1", "10.27")) with { ObservedAtUtc = withMillis },
            Rules(Rule("P1", MetricCodes.Pm10)));

        Assert.True(result.Succeeded);

        // A key built from one timestamp and a row stored with another would mean
        // the upsert could never match its own previous write.
        Assert.Equal(0, result.Reading!.ObservedAtUtc.Millisecond);
        Assert.Equal(
            MappingEngine.BuildExternalKey(1000, result.Reading.ObservedAtUtc),
            result.Reading.ExternalKey);
    }
}
