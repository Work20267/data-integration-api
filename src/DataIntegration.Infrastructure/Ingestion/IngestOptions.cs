using System.ComponentModel.DataAnnotations;

namespace DataIntegration.Infrastructure.Ingestion;

/// <summary>
/// Source-independent ingest tuning. Kept separate from
/// <see cref="Sources.SensorCommunity.SensorCommunityOptions"/> so the pipeline
/// does not depend on any particular source's configuration.
/// </summary>
public class IngestOptions
{
    public const string SectionName = "Ingest";

    /// <summary>
    /// Rows buffered before each SqlBulkCopy round trip. Larger means fewer trips
    /// and more memory held at once; 2,000 keeps a chunk well under a megabyte for
    /// these payloads.
    /// </summary>
    [Range(100, 20_000)]
    public int StagingChunkSize { get; set; } = 2_000;
}
