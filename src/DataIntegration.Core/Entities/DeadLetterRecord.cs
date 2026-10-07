namespace DataIntegration.Core.Entities;

/// <summary>
/// A row that could not be transformed. Written per row, not per batch — one bad
/// row out of ten thousand must not reject the other nine thousand nine hundred
/// and ninety-nine.
/// </summary>
public class DeadLetterRecord
{
    public long Id { get; set; }

    public Guid IngestBatchId { get; set; }
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>The offending row, verbatim, so a replay needs nothing else.</summary>
    public string RawRow { get; set; } = string.Empty;

    /// <summary>Stable, machine-readable reason — groupable in a metrics dashboard.</summary>
    public string ErrorCode { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public DeadLetterStatus Status { get; set; }
    public int ReplayCount { get; set; }
    public DateTime? LastReplayAtUtc { get; set; }
}
