using Microsoft.Extensions.Logging;

namespace DataIntegration.Infrastructure.Logging;

/// <summary>
/// Source-generated log messages.
/// </summary>
/// <remarks>
/// The generator emits a strongly-typed, allocation-free call site per message,
/// so arguments are never boxed and nothing is formatted when the level is
/// disabled. This is what CA1848 and CA1873 ask for, and it keeps the ingest
/// hot path free of logging overhead.
/// </remarks>
internal static partial class InfrastructureLog
{
    // ---- upstream readers (1000-1099) ----
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "Fetching live feed from {url}")]
    public static partial void FetchingLiveFeed(ILogger logger, Uri url);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Live feed yielded {count} records")]
    public static partial void LiveFeedYielded(ILogger logger, int count);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Live feed exceeded the {cap} record cap; stopping early")]
    public static partial void LiveFeedCapExceeded(ILogger logger, int cap);

    // ---- ingest pipeline (1100-1199) ----
    [LoggerMessage(EventId = 1100, Level = LogLevel.Information,
        Message = "Idempotency key {key} already processed as batch {batchId}; skipping")]
    public static partial void IdempotentSkip(ILogger logger, string key, Guid batchId);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information,
        Message = "Batch {batchId} staged {rows} rows from {source} in {seconds}s")]
    public static partial void BatchStaged(
        ILogger logger, Guid batchId, int rows, string source, double seconds);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Error,
        Message = "Batch {batchId} failed after staging {rows} rows from {source}; rows retained for reprocessing")]
    public static partial void BatchFailed(
        ILogger logger, Exception exception, Guid batchId, int rows, string source);

    // ---- transformation (1200-1299) ----
    [LoggerMessage(EventId = 1200, Level = LogLevel.Information,
        Message = "Batch {batchId} transformed: {read} read, {canonical} canonical, {deadLettered} dead-lettered in {seconds}s")]
    public static partial void BatchTransformed(
        ILogger logger, Guid batchId, int read, int canonical, int deadLettered, double seconds);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information,
        Message = "Dead letter {id} replayed into batch {batchId}")]
    public static partial void DeadLetterReplayed(ILogger logger, long id, Guid batchId);
}
