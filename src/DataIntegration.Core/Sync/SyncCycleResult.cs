namespace DataIntegration.Core.Sync;

public enum SyncOutcome
{
    /// <summary>Ingested and transformed.</summary>
    Completed = 0,

    /// <summary>This window had already been ingested; nothing was re-fetched.</summary>
    SkippedIdempotent = 1,

    /// <summary>The cycle failed. The worker continues; the next cycle retries.</summary>
    Failed = 2,
}

/// <param name="Error">Set only when <see cref="SyncOutcome.Failed"/>.</param>
public sealed record SyncCycleResult(
    string SourceKey,
    SyncOutcome Outcome,
    Guid? BatchId = null,
    int RowsStaged = 0,
    int RowsCanonical = 0,
    int RowsDeadLettered = 0,
    string? Error = null);
