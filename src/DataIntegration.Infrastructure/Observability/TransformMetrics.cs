using System.Diagnostics;
using System.Diagnostics.Metrics;
using DataIntegration.Core.Observability;

namespace DataIntegration.Infrastructure.Observability;

/// <summary>
/// Transformation instruments. The dead-letter counter is tagged by error code so
/// an operator can see at a glance whether 400 failures are one problem or four.
/// </summary>
public sealed class TransformMetrics
{
    private readonly Counter<long> _canonical;
    private readonly Counter<long> _deadLettered;
    private readonly Histogram<double> _duration;

    public TransformMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(DiagnosticNames.Meter);

        _canonical = meter.CreateCounter<long>(
            "di.transform.rows_canonical", "{row}", "Rows written to the canonical model.");

        _deadLettered = meter.CreateCounter<long>(
            "di.transform.rows_dead_lettered", "{row}", "Rows that could not be transformed.");

        _duration = meter.CreateHistogram<double>(
            "di.transform.duration", "s", "Wall-clock duration of a batch transformation.");
    }

    public void Canonical(string sourceKey, long count) =>
        _canonical.Add(count, new KeyValuePair<string, object?>(DiagnosticNames.Tags.SourceKey, sourceKey));

    public void DeadLettered(string sourceKey, string errorCode, long count)
    {
        var tags = new TagList
        {
            { DiagnosticNames.Tags.SourceKey, sourceKey },
            { DiagnosticNames.Tags.ErrorCode, errorCode },
        };

        _deadLettered.Add(count, tags);
    }

    public void BatchDuration(string sourceKey, double seconds) =>
        _duration.Record(seconds, new KeyValuePair<string, object?>(DiagnosticNames.Tags.SourceKey, sourceKey));
}
