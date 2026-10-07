using System.Diagnostics;
using DataIntegration.Core.Entities;
using DataIntegration.Core.Observability;
using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Logging;
using DataIntegration.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataIntegration.Infrastructure.Transformation;

/// <summary>
/// Transforms a batch's staged rows into canonical readings, dead-lettering the
/// ones that cannot be transformed.
/// </summary>
/// <remarks>
/// The guarantee this class exists to provide: <b>one bad row out of 17,770 must
/// not reject the other 17,769.</b> Every row is transformed inside its own
/// failure boundary, and a batch is expected to finish with a partial outcome.
/// </remarks>
public class TransformService(
    IEnumerable<IPayloadParser> parsers,
    IMappingRuleProvider ruleProvider,
    IStagingReader staging,
    ICanonicalWriter canonical,
    IDeadLetterStore deadLetters,
    TransformMetrics metrics,
    IOptions<TransformOptions> options,
    ILogger<TransformService> logger)
{
    private static readonly ActivitySource Activity = new(DiagnosticNames.ActivitySource);

    public async Task<TransformSummary> TransformBatchAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var parserByKey = parsers.ToDictionary(p => p.SourceKey, StringComparer.Ordinal);
        var ruleSets = new Dictionary<string, MappingRuleSet>(StringComparer.Ordinal);
        var chunkSize = options.Value.ChunkSize;

        using var activity = Activity.StartActivity("transform", ActivityKind.Internal);
        activity?.SetTag(DiagnosticNames.Tags.BatchId, batchId);

        var stopwatch = Stopwatch.StartNew();
        var afterId = 0L;
        var rowsRead = 0;
        var rowsCanonical = 0;
        var rowsDeadLettered = 0;
        var sourceKey = "unknown";

        while (true)
        {
            var page = await staging.ReadPendingPageAsync(
                batchId, afterId, chunkSize, cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            var readings = new List<CanonicalReading>(page.Count);
            var failures = new List<DeadLetterEntry>();
            var transformedIds = new List<long>(page.Count);
            var rejectedIds = new List<long>();

            foreach (var row in page)
            {
                afterId = row.Id;
                rowsRead++;
                sourceKey = row.SourceKey;

                if (!ruleSets.TryGetValue(row.SourceKey, out var rules))
                {
                    rules = await ruleProvider.GetAsync(row.SourceKey, cancellationToken);
                    ruleSets[row.SourceKey] = rules;
                }

                var outcome = TransformRow(row, rules, parserByKey);

                if (outcome.Succeeded)
                {
                    readings.Add(outcome.Reading!);
                    transformedIds.Add(row.Id);
                }
                else
                {
                    failures.Add(new DeadLetterEntry(
                        row.SourceKey,
                        row.RawPayload,
                        outcome.Failure!.ErrorCode,
                        outcome.Failure.Message));

                    rejectedIds.Add(row.Id);
                }
            }

            if (readings.Count > 0)
            {
                await canonical.UpsertAsync(readings, cancellationToken);
                await staging.MarkAsync(transformedIds, StagingStatus.Transformed, cancellationToken);

                metrics.Canonical(sourceKey, readings.Count);
                rowsCanonical += readings.Count;
            }

            if (failures.Count > 0)
            {
                await deadLetters.WriteAsync(batchId, failures, cancellationToken);
                await staging.MarkAsync(rejectedIds, StagingStatus.Rejected, cancellationToken);

                foreach (var group in failures.GroupBy(f => f.ErrorCode, StringComparer.Ordinal))
                {
                    metrics.DeadLettered(sourceKey, group.Key, group.Count());
                }

                rowsDeadLettered += failures.Count;
            }
        }

        stopwatch.Stop();
        var seconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 2);

        metrics.BatchDuration(sourceKey, stopwatch.Elapsed.TotalSeconds);

        InfrastructureLog.BatchTransformed(
            logger, batchId, rowsRead, rowsCanonical, rowsDeadLettered, seconds);

        return new TransformSummary(
            batchId, rowsRead, rowsCanonical, rowsDeadLettered, stopwatch.Elapsed);
    }

    /// <summary>
    /// Transforms one row. Never throws.
    /// </summary>
    /// <remarks>
    /// The catch-all is the point of this method, not an oversight. These payloads
    /// come from hobbyist firmware, and an unanticipated shape must cost exactly
    /// one dead letter — not the remaining 17,769 rows of the batch. The exception
    /// is recorded on the dead letter so the cause is still diagnosable.
    /// </remarks>
    private static TransformResult TransformRow(
        StagedRow row,
        MappingRuleSet rules,
        Dictionary<string, IPayloadParser> parserByKey)
    {
#pragma warning disable CA1031 // Deliberate: per-row isolation is this method's contract.
        try
        {
            if (!parserByKey.TryGetValue(row.SourceKey, out var parser))
            {
                return TransformResult.Failed(
                    TransformErrorCodes.NoParserForSource,
                    $"No parser is registered for source '{row.SourceKey}'.");
            }

            var parsed = parser.Parse(row.RawPayload);

            if (!parsed.Succeeded)
            {
                return TransformResult.Failed(
                    parsed.Failure!.ErrorCode, parsed.Failure.Message);
            }

            return MappingEngine.Apply(row.SourceKey, parsed.Reading!, rules);
        }
        catch (Exception ex)
        {
            return TransformResult.Failed(
                TransformErrorCodes.PayloadMalformed,
                $"Unhandled {ex.GetType().Name} while transforming staging row {row.Id}: {ex.Message}");
        }
#pragma warning restore CA1031
    }
}
