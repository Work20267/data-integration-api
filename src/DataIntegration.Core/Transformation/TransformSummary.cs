namespace DataIntegration.Core.Transformation;

/// <summary>
/// Outcome of transforming one batch.
/// </summary>
/// <remarks>
/// Both counts are reported because a batch is expected to have a partial
/// outcome. "17,600 canonical, 170 dead-lettered" is a success, not a failure,
/// and the caller needs to see both numbers to know that.
/// </remarks>
public sealed record TransformSummary(
    Guid BatchId,
    int RowsRead,
    int RowsCanonical,
    int RowsDeadLettered,
    TimeSpan Duration);
