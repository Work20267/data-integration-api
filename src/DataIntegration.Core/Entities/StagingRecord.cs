namespace DataIntegration.Core.Entities;

/// <summary>
/// A raw upstream row, persisted verbatim before any transformation runs.
/// Keeping it means a mapping bug can be fixed and the batch reprocessed without
/// re-fetching from a volunteer-funded upstream.
/// </summary>
public class StagingRecord
{
    public long Id { get; set; }

    public Guid IngestBatchId { get; set; }
    public IngestBatch? IngestBatch { get; set; }

    public string SourceKey { get; set; } = string.Empty;

    /// <summary>The untouched source row: a JSON object, or one CSV line with its header.</summary>
    public string RawPayload { get; set; } = string.Empty;

    /// <summary>Sensor model as the source reported it, used to select mapping rules.</summary>
    public string? SensorModel { get; set; }

    public DateTime ReceivedAtUtc { get; set; }

    public StagingStatus Status { get; set; }
}
