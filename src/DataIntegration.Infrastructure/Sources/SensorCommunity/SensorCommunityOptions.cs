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

    // ---- archive source ----------------------------------------------------

    /// <summary>
    /// Day to ingest from the archive. Null means yesterday (UTC) — today's files
    /// do not exist yet, since the archive is published a day behind.
    /// </summary>
    public DateOnly? ArchiveDate { get; set; }

    /// <summary>
    /// Files fetched per ingest. A single day holds ~17,800 of them, and pulling
    /// them all would be abusive to a volunteer-funded host — so this is a hard
    /// cap, not a page size. See ADR 001.
    /// </summary>
    [Range(1, 500)]
    public int ArchiveMaxFiles { get; set; } = 25;

    /// <summary>
    /// Pause between file requests. Deliberate politeness rather than tuning:
    /// 25 files at 250 ms costs us six seconds and the host almost nothing.
    /// </summary>
    [Range(0, 5_000)]
    public int ArchiveFetchDelayMs { get; set; } = 250;

    /// <summary>
    /// Restricts which sensor models are fetched, by the token in the filename
    /// (<c>sds011</c>, <c>bme280</c>, <c>dht22</c>…). Empty means any.
    /// </summary>
    public IList<string> ArchiveSensorModels { get; } = [];
}
