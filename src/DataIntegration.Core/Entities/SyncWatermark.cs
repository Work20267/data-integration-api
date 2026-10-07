namespace DataIntegration.Core.Entities;

/// <summary>
/// How far a source has been consumed. Lets an interrupted sync resume from the
/// last committed position instead of restarting the whole history.
/// </summary>
public class SyncWatermark
{
    /// <summary>Primary key — one watermark per source.</summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>Highest observation timestamp successfully written to canonical.</summary>
    public DateTime? LastObservedAtUtc { get; set; }

    /// <summary>Tie-breaker for readings sharing <see cref="LastObservedAtUtc"/>.</summary>
    public string? LastExternalKey { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
