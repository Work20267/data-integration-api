namespace DataIntegration.Core.Transformation;

public enum ReplayStatus
{
    /// <summary>No dead letter with that id.</summary>
    NotFound = 0,

    /// <summary>Already replayed successfully; replaying again would be a no-op.</summary>
    AlreadyReplayed = 1,

    /// <summary>
    /// Re-transformed and written to the canonical model. The usual path after a
    /// mapping rule has been corrected.
    /// </summary>
    Replayed = 2,

    /// <summary>
    /// Still fails, with the current reason attached. The row stays pending so it
    /// can be replayed again once the actual cause is fixed.
    /// </summary>
    StillFailing = 3,
}

/// <param name="ErrorCode">Set only when <see cref="ReplayStatus.StillFailing"/>.</param>
public sealed record ReplayOutcome(
    ReplayStatus Status,
    string? ErrorCode = null,
    string? Message = null);
