namespace DataIntegration.Core.Transformation;

/// <summary>
/// Why one row could not be turned into a canonical reading.
/// </summary>
/// <param name="ErrorCode">A value from <see cref="TransformErrorCodes"/>.</param>
/// <param name="Message">
/// Human-readable detail, including the offending value where that helps someone
/// fix it. Capped at the dead-letter column width by the writer.
/// </param>
public sealed record TransformFailure(string ErrorCode, string Message);
