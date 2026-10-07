using DataIntegration.Core;
using DataIntegration.Core.Entities;
using DataIntegration.Core.Transformation;

namespace DataIntegration.IntegrationTests;

/// <summary>
/// Supplies rules directly, so these tests exercise SQL Server rather than the
/// rule-loading path (which the unit tests already cover).
/// </summary>
internal sealed class StaticMappingRuleProvider(MappingRuleSet ruleSet) : IMappingRuleProvider
{
    public Task<MappingRuleSet> GetAsync(string sourceKey, CancellationToken cancellationToken) =>
        Task.FromResult(ruleSet);

    public void Invalidate() { }
}

/// <summary>
/// Wraps the real writer and fails once, after a chosen number of chunks.
/// </summary>
/// <remarks>
/// This is the controlled half of the chaos test. Killing the container proves
/// the system survives connection loss; failing deterministically here proves
/// the far more important property — that work already committed is retained and
/// a retry completes without duplicating it — and proves it the same way on
/// every run, which a timing-dependent kill cannot.
/// </remarks>
internal sealed class FailAfterChunksWriter(ICanonicalWriter inner, int failAfterChunks)
    : ICanonicalWriter
{
    private int _chunks;

    public bool HasFailed { get; private set; }

    public async Task<int> UpsertAsync(
        IReadOnlyList<CanonicalReading> readings, CancellationToken cancellationToken)
    {
        if (_chunks >= failAfterChunks && !HasFailed)
        {
            HasFailed = true;

            throw new TimeoutException(
                $"simulated downstream failure after {_chunks} chunk(s)");
        }

        _chunks++;

        return await inner.UpsertAsync(readings, cancellationToken);
    }
}

internal static class TestData
{
    /// <summary>Rules enough to resolve the payloads these tests stage.</summary>
    public static MappingRuleSet LiveRules() =>
        new(SourceKeys.JsonLive,
        [
            new MappingRule(SourceKeys.JsonLive, null, "P1", MetricCodes.Pm10, false),
            new MappingRule(SourceKeys.JsonLive, null, "P2", MetricCodes.Pm25, false),
            new MappingRule(SourceKeys.JsonLive, null, "temperature", MetricCodes.Temperature, false),
        ]);

    public static MappingRuleSet ArchiveRules() =>
        new(SourceKeys.CsvArchive,
        [
            new MappingRule(SourceKeys.CsvArchive, null, "P1", MetricCodes.Pm10, false),
            new MappingRule(SourceKeys.CsvArchive, null, "P2", MetricCodes.Pm25, false),
        ]);

    /// <summary>
    /// The same observation as <see cref="LivePayload"/>, in the archive's format:
    /// semicolon-delimited, ISO-T timestamp, no country or indoor flag. Header
    /// plus data line, exactly as the reader stages it.
    /// </summary>
    public static string ArchivePayload(int sensorId, DateTime observedAtUtc, decimal pm10) =>
        "sensor_id;sensor_type;location;lat;lon;timestamp;P1;durP1;ratioP1;P2;durP2;ratioP2\n"
        + $"{sensorId};SDS011;500;48.4;11.3;{observedAtUtc:yyyy-MM-ddTHH:mm:ss};{pm10};;;9.17;;";

    /// <summary>
    /// A live-feed payload for a given sensor and second. Deterministic, so the
    /// same inputs always produce the same ExternalKey.
    /// </summary>
    public static string LivePayload(int sensorId, DateTime observedAtUtc, decimal pm10) =>
        $$"""
        {
          "timestamp": "{{observedAtUtc:yyyy-MM-dd HH:mm:ss}}",
          "location": { "id": 500, "latitude": "48.4", "longitude": "11.3", "country": "DE", "indoor": 0 },
          "sensor": { "id": {{sensorId}}, "sensor_type": { "name": "SDS011" } },
          "sensordatavalues": [
            { "value": "{{pm10}}", "value_type": "P1" },
            { "value": "9.17", "value_type": "P2" }
          ]
        }
        """;
}
