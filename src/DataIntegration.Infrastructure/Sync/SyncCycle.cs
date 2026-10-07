using DataIntegration.Core.Sync;
using DataIntegration.Infrastructure.Ingestion;
using DataIntegration.Infrastructure.Logging;
using DataIntegration.Infrastructure.Transformation;
using Microsoft.Extensions.Logging;

namespace DataIntegration.Infrastructure.Sync;

/// <summary>
/// One sync cycle for one source: ingest, transform, advance the watermark.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="SyncWorker"/>. The worker owns timing
/// and nothing else, so this logic is testable without a clock — and the
/// never-throws contract is enforced in one place rather than scattered through
/// a loop.
/// </remarks>
public class SyncCycle(
    IngestService ingest,
    TransformService transform,
    ISyncWatermarkStore watermarks,
    ILogger<SyncCycle> logger)
{
    /// <summary>
    /// Runs a cycle. Never throws except on cancellation.
    /// </summary>
    /// <remarks>
    /// An unhandled exception escaping a BackgroundService stops it silently, or
    /// brings the host down, depending on configuration. Either way one bad cycle
    /// would end all future syncing — so failure is a return value here, not an
    /// exception.
    /// </remarks>
    public async Task<SyncCycleResult> RunAsync(
        string sourceKey,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
#pragma warning disable CA1031 // Deliberate: a failed cycle must not end the worker.
        try
        {
            var ingested = await ingest.IngestAsync(sourceKey, idempotencyKey, cancellationToken);

            if (ingested.WasAlreadyProcessed)
            {
                return new SyncCycleResult(
                    sourceKey,
                    SyncOutcome.SkippedIdempotent,
                    ingested.BatchId,
                    ingested.RowsStaged);
            }

            var summary = await transform.TransformBatchAsync(ingested.BatchId, cancellationToken);

            await watermarks.AdvanceAsync(sourceKey, cancellationToken);

            return new SyncCycleResult(
                sourceKey,
                SyncOutcome.Completed,
                ingested.BatchId,
                ingested.RowsStaged,
                summary.RowsCanonical,
                summary.RowsDeadLettered);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown, not failure. Let it propagate so the host stops promptly.
            throw;
        }
        catch (Exception ex)
        {
            InfrastructureLog.SyncCycleFailed(logger, ex, sourceKey);

            return new SyncCycleResult(
                sourceKey, SyncOutcome.Failed, Error: $"{ex.GetType().Name}: {ex.Message}");
        }
#pragma warning restore CA1031
    }
}
