using System.ComponentModel.DataAnnotations;

namespace DataIntegration.Infrastructure.Sources.SensorCommunity;

/// <summary>
/// Configuration for the Sensor.Community sources.
/// </summary>
/// <remarks>
/// The upstream is volunteer-funded, which is why there is a record cap and a
/// deliberately modest timeout here rather than "fetch everything as fast as
/// possible". See ADR 001.
/// </remarks>
public class SensorCommunityOptions
{
    public const string SectionName = "SensorCommunity";

    /// <summary>Last five minutes of readings across the whole network.</summary>
    [Required]
    public Uri LiveFeedUrl { get; set; } = new("https://data.sensor.community/static/v2/data.json");

    /// <summary>Daily CSV archive root. Phase 5 uses this; Phase 3 does not.</summary>
    [Required]
    public Uri ArchiveBaseUrl { get; set; } = new("https://archive.sensor.community/");

    /// <summary>
    /// The feed is around 8.5 MB, so this is generous on purpose — a slow read is
    /// not a failure, and retrying a large download wastes upstream bandwidth.
    /// </summary>
    [Range(5, 300)]
    public int RequestTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Rows buffered before each SqlBulkCopy round trip. Larger means fewer trips
    /// and more memory held at once.
    /// </summary>
    [Range(100, 20_000)]
    public int StagingChunkSize { get; set; } = 2_000;

    /// <summary>
    /// Safety cap per ingest call. A normal window is ~17,700 records; if the feed
    /// ever returns far more, stop rather than run unbounded.
    /// </summary>
    [Range(100, 1_000_000)]
    public int MaxRecordsPerCall { get; set; } = 100_000;
}
