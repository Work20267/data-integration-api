using DataIntegration.Core.Entities;
using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace DataIntegration.Infrastructure.Transformation;

/// <summary>
/// Re-transforms a single dead-lettered row.
/// </summary>
/// <remarks>
/// This is what makes the dead letter a queue rather than a graveyard. The usual
/// sequence is: a batch dead-letters rows for a sensor model with no mapping
/// rules, rules are added, and the rows are replayed — with no need to re-fetch
/// from upstream, because the raw payload was preserved at ingest.
///
/// Replay re-reads the rules through the provider, so a rule added after the
/// failure is picked up — provided the cache has been invalidated.
/// </remarks>
public class DeadLetterReplayService(
    IEnumerable<IPayloadParser> parsers,
    IMappingRuleProvider ruleProvider,
    IDeadLetterStore deadLetters,
    ICanonicalWriter canonical,
    ILogger<DeadLetterReplayService> logger)
{
    public async Task<ReplayOutcome> ReplayAsync(long id, CancellationToken cancellationToken)
    {
        var record = await deadLetters.GetAsync(id, cancellationToken);

        if (record is null)
        {
            return new ReplayOutcome(ReplayStatus.NotFound);
        }

        if (record.Status == DeadLetterStatus.Replayed)
        {
            return new ReplayOutcome(ReplayStatus.AlreadyReplayed);
        }

        var parser = parsers.FirstOrDefault(p => p.SourceKey == record.SourceKey);

        if (parser is null)
        {
            return new ReplayOutcome(
                ReplayStatus.StillFailing,
                TransformErrorCodes.NoParserForSource,
                $"No parser is registered for source '{record.SourceKey}'.");
        }

        var parsed = parser.Parse(record.RawRow);

        if (!parsed.Succeeded)
        {
            return new ReplayOutcome(
                ReplayStatus.StillFailing,
                parsed.Failure!.ErrorCode,
                parsed.Failure.Message);
        }

        var rules = await ruleProvider.GetAsync(record.SourceKey, cancellationToken);
        var result = MappingEngine.Apply(record.SourceKey, parsed.Reading!, rules);

        if (!result.Succeeded)
        {
            return new ReplayOutcome(
                ReplayStatus.StillFailing,
                result.Failure!.ErrorCode,
                result.Failure.Message);
        }

        await canonical.UpsertAsync([result.Reading!], cancellationToken);
        await deadLetters.MarkReplayedAsync(id, cancellationToken);

        InfrastructureLog.DeadLetterReplayed(logger, id, record.IngestBatchId);

        return new ReplayOutcome(ReplayStatus.Replayed);
    }
}
