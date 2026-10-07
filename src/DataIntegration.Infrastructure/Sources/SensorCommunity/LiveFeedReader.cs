using System.Runtime.CompilerServices;
using System.Text.Json;
using DataIntegration.Core;
using DataIntegration.Core.Ingestion;
using DataIntegration.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataIntegration.Infrastructure.Sources.SensorCommunity;

/// <summary>
/// Reads the Sensor.Community live JSON feed.
/// </summary>
/// <remarks>
/// Phase 3 deliberately does not interpret the payload. Each array element is
/// stored as its own raw row, with only the sensor model lifted out so Phase 4
/// can select mapping rules. Understanding the measurements is Phase 4's job.
/// </remarks>
public class LiveFeedReader(
    HttpClient httpClient,
    IOptions<SensorCommunityOptions> options,
    ILogger<LiveFeedReader> logger) : IUpstreamReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly SensorCommunityOptions _options = options.Value;

    public string SourceKey => SourceKeys.JsonLive;

    public async IAsyncEnumerable<RawRow> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        InfrastructureLog.FetchingLiveFeed(logger, _options.LiveFeedUrl);

        // ResponseHeadersRead means we start parsing while the body is still
        // arriving, instead of buffering ~8.5 MB before doing any work.
        using var response = await httpClient.GetAsync(
            _options.LiveFeedUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var count = 0;

        await foreach (var element in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(
            stream, SerializerOptions, cancellationToken))
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (++count > _options.MaxRecordsPerCall)
            {
                InfrastructureLog.LiveFeedCapExceeded(logger, _options.MaxRecordsPerCall);
                yield break;
            }

            // GetRawText preserves the element byte-for-byte as the source wrote it.
            yield return new RawRow(SourceKeys.JsonLive, ReadSensorModel(element), element.GetRawText());
        }

        InfrastructureLog.LiveFeedYielded(logger, count);
    }

    /// <summary>
    /// Lifts sensor.sensor_type.name, tolerating every level being absent — these
    /// are hobbyist devices and the feed is not guaranteed to be well-formed.
    /// </summary>
    private static string? ReadSensorModel(JsonElement element) =>
        element.TryGetProperty("sensor", out var sensor)
        && sensor.ValueKind == JsonValueKind.Object
        && sensor.TryGetProperty("sensor_type", out var sensorType)
        && sensorType.ValueKind == JsonValueKind.Object
        && sensorType.TryGetProperty("name", out var name)
        && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;
}
