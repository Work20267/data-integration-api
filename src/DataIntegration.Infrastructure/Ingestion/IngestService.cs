using System.Diagnostics;
using DataIntegration.Core.Ingestion;
using DataIntegration.Core.Observability;
using DataIntegration.Infrastructure.Logging;
using DataIntegration.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataIntegration.Infrastructure.Ingestion;

/// <summary>
/// Orchestrates one ingest: resolve the source, open a batch, stream rows into
/// staging in chunks, and close the batch.
/// </summary>
/// <remarks>
/// Nothing here interprets the payload. Transformation into the canonical model
/// is Phase 4, and keeping the two separate is what makes a mapping fix
/// reprocessable without re-fetching from upstream.
/// </remarks>
public class IngestService(
    IEnumerable<IUpstreamReader> readers,
    IIngestBatchStore batches,
    IStagingWriter staging,
    IngestMetrics metrics,
    IOptions<IngestOptions> options,
    ILogger<IngestService> logger)
{
    private static readonly ActivitySource Activity = new(DiagnosticNames.ActivitySource);

    public async Task<IngestResult> IngestAsync(
        string sourceKey,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Resolve the source first. An unrecognised source is a bad request, and
        // rejecting it here costs nothing — checking idempotency first would spend
        // a database round trip on a request that can never succeed.
        var reader = readers.FirstOrDefault(r => r.SourceKey == sourceKey)
            ?? throw new UnknownSourceException(sourceKey);

        // Then idempotency, before any work: a replayed request must be recognised
        // and reported, never processed a second time.
        var existing = await batches.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            InfrastructureLog.IdempotentSkip(logger, idempotencyKey, existing.Id);

            metrics.BatchFinished(existing.SourceKey, "skipped-idempotent", 0);

            return IngestResult.AlreadyProcessed(
                existing.Id, existing.SourceKey, existing.RowsReceived);
        }

        using var activity = Activity.StartActivity("ingest", ActivityKind.Internal);
        activity?.SetTag(DiagnosticNames.Tags.SourceKey, sourceKey);

        var batch = await batches.StartAsync(sourceKey, idempotencyKey, cancellationToken);
        activity?.SetTag(DiagnosticNames.Tags.BatchId, batch.Id);

        var stopwatch = Stopwatch.StartNew();
        var chunkSize = options.Value.StagingChunkSize;
        var buffer = new List<RawRow>(chunkSize);
        var staged = 0;

        try
        {
            await foreach (var row in reader.ReadAsync(cancellationToken))
            {
                buffer.Add(row);

                if (buffer.Count >= chunkSize)
                {
                    staged += await staging.WriteChunkAsync(batch.Id, buffer, cancellationToken);
                    buffer.Clear();
                }
            }

            if (buffer.Count > 0)
            {
                staged += await staging.WriteChunkAsync(batch.Id, buffer, cancellationToken);
            }

            await batches.CompleteAsync(batch.Id, staged, cancellationToken);
            stopwatch.Stop();

            var elapsed = stopwatch.Elapsed;

            // Rounded once into a local: passing Math.Round() as a log argument
            // would evaluate it even when the level is disabled (CA1873).
            var elapsedSeconds = Math.Round(elapsed.TotalSeconds, 2);

            metrics.RowsStaged(sourceKey, staged);
            metrics.BatchFinished(sourceKey, "completed", elapsed.TotalSeconds);

            InfrastructureLog.BatchStaged(logger, batch.Id, staged, sourceKey, elapsedSeconds);

            return IngestResult.Completed(batch.Id, sourceKey, staged, elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Rows already written to staging are kept deliberately. A partial
            // batch is reprocessable; a deleted one is lost, and re-fetching from
            // a volunteer-funded upstream is the thing we are trying to avoid.
            await batches.FailAsync(batch.Id, staged, CancellationToken.None);

            metrics.RowsStaged(sourceKey, staged);
            metrics.BatchFinished(sourceKey, "failed", stopwatch.Elapsed.TotalSeconds);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            InfrastructureLog.BatchFailed(logger, ex, batch.Id, staged, sourceKey);

            throw;
        }
    }
}
