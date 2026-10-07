namespace DataIntegration.Core.Ingestion;

/// <summary>
/// Thrown when a request names a source that has no registered reader. Surfaced
/// as a 404 rather than a 500: the caller asked for something that does not
/// exist, which is not a server fault.
/// </summary>
public class UnknownSourceException : Exception
{
    public UnknownSourceException(string sourceKey)
        : base($"No reader is registered for source '{sourceKey}'.")
        => SourceKey = sourceKey;

    public UnknownSourceException() { }

    public UnknownSourceException(string message, Exception innerException)
        : base(message, innerException) { }

    public string? SourceKey { get; }
}
