using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DataIntegration.Core;
using DataIntegration.Core.Ingestion;
using DataIntegration.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataIntegration.Infrastructure.Sources.SensorCommunity;

/// <summary>
/// Reads the Sensor.Community daily CSV archive.
/// </summary>
/// <remarks>
/// <para>
/// A day holds roughly 17,800 files, one per sensor. This reader deliberately
/// fetches a small, capped subset with a pause between requests: the host is
/// volunteer-funded, and during development it returned an outright connection
/// failure under light load. Politeness is a correctness concern here, not
/// etiquette.
/// </para>
/// <para>
/// Each emitted row carries the file's header line followed by its own data line,
/// so a staged row is self-describing — it can be replayed years later without
/// the file it came from still existing.
/// </para>
/// </remarks>
public partial class ArchiveReader(
    HttpClient httpClient,
    IOptions<SensorCommunityOptions> options,
    ILogger<ArchiveReader> logger) : IUpstreamReader
{
    private readonly SensorCommunityOptions _options = options.Value;

    public string SourceKey => SourceKeys.CsvArchive;

    /// <summary>Matches the anchors in the archive's directory listing.</summary>
    [GeneratedRegex(
        @"href=""(?<file>\d{4}-\d{2}-\d{2}_(?<model>[a-z0-9]+)_sensor_\d+\.csv)""",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex CsvLinkPattern { get; }

    public async IAsyncEnumerable<RawRow> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var date = _options.ArchiveDate
                   ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        var dateToken = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dayUrl = new Uri(_options.ArchiveBaseUrl, $"{dateToken}/");

        InfrastructureLog.ArchiveListing(logger, dayUrl);

        var files = await ListFilesAsync(dayUrl, cancellationToken);

        if (files.Count == 0)
        {
            InfrastructureLog.ArchiveEmpty(logger, dateToken);
            yield break;
        }

        InfrastructureLog.ArchiveSelected(logger, files.Count, dateToken);

        var rows = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileUrl = new Uri(dayUrl, file);
            string content;

            try
            {
                content = await httpClient.GetStringAsync(fileUrl, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                // One unavailable file must not abandon the other 24. The batch
                // reports fewer rows; it does not fail.
                InfrastructureLog.ArchiveFileSkipped(logger, ex, file);
                continue;
            }

            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length < 2)
            {
                continue;
            }

            var header = lines[0].TrimEnd('\r');
            var model = ReadModelFromHeaderRow(lines[1]);

            foreach (var line in lines.Skip(1))
            {
                var dataLine = line.TrimEnd('\r');

                if (dataLine.Length == 0)
                {
                    continue;
                }

                if (++rows > _options.MaxRecordsPerCall)
                {
                    InfrastructureLog.LiveFeedCapExceeded(logger, _options.MaxRecordsPerCall);
                    yield break;
                }

                // Header plus the row, so the staged payload needs no external
                // context to be parsed or replayed later.
                yield return new RawRow(
                    SourceKeys.CsvArchive, model, $"{header}\n{dataLine}");
            }

            if (_options.ArchiveFetchDelayMs > 0)
            {
                await Task.Delay(_options.ArchiveFetchDelayMs, cancellationToken);
            }
        }

        InfrastructureLog.ArchiveCompleted(logger, rows, files.Count);
    }

    private async Task<List<string>> ListFilesAsync(Uri dayUrl, CancellationToken cancellationToken)
    {
        // The listing is ~4.5 MB of HTML. It is fetched once per ingest, which is
        // the cheapest way to discover which sensors reported that day.
        var listing = await httpClient.GetStringAsync(dayUrl, cancellationToken);

        var wanted = _options.ArchiveSensorModels;

        return CsvLinkPattern.Matches(listing)
            .Select(m => (File: m.Groups["file"].Value, Model: m.Groups["model"].Value))
            .Where(x => wanted.Count == 0
                        || wanted.Contains(x.Model, StringComparer.OrdinalIgnoreCase))
            .Select(x => x.File)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .Take(_options.ArchiveMaxFiles)
            .ToList();
    }

    /// <summary>
    /// Takes the model from the data row's <c>sensor_type</c> column rather than
    /// the filename: the filename token is lowercased (<c>sds011</c>) while the
    /// column holds the real name (<c>SDS011</c>), and mapping rules are matched
    /// against the latter.
    /// </summary>
    private static string? ReadModelFromHeaderRow(string firstDataLine)
    {
        var parts = firstDataLine.Split(';');

        return parts.Length > 1 && parts[1].Length > 0 ? parts[1].Trim() : null;
    }
}
