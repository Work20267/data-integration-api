namespace DataIntegration.Core.Entities;

/// <summary>Lifecycle of a single ingest operation.</summary>
public enum BatchStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2,
}

/// <summary>Lifecycle of one staged raw row.</summary>
public enum StagingStatus
{
    /// <summary>Persisted, not yet transformed.</summary>
    Pending = 0,
    Transformed = 1,
    /// <summary>Transformation failed; a dead letter exists for this row.</summary>
    Rejected = 2,
}

/// <summary>Lifecycle of a dead-lettered row.</summary>
public enum DeadLetterStatus
{
    Pending = 0,
    Replayed = 1,
    Discarded = 2,
}
