using System.Diagnostics;
using System.Diagnostics.Metrics;
using DataIntegration.Core.Observability;

namespace DataIntegration.Infrastructure.Observability;

/// <summary>
/// Ingest instruments, created through <see cref="IMeterFactory"/> so the meter is
/// owned by the container rather than a static field — which keeps tests isolated
/// from each other.
/// </summary>
public sealed class IngestMetrics
{
    private readonly Counter<long> _rowsStaged;
    private readonly Counter<long> _batches;
    private readonly Histogram<double> _duration;

    public IngestMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(DiagnosticNames.Meter);

        _rowsStaged = meter.CreateCounter<long>(
            "di.ingest.rows_staged", "{row}", "Raw rows written to staging.");

        _batches = meter.CreateCounter<long>(
            "di.ingest.batches", "{batch}", "Ingest batches, tagged by outcome.");

        _duration = meter.CreateHistogram<double>(
            "di.ingest.duration", "s", "Wall-clock duration of an ingest operation.");
    }

    public void RowsStaged(string sourceKey, long count) =>
        _rowsStaged.Add(count, new KeyValuePair<string, object?>(DiagnosticNames.Tags.SourceKey, sourceKey));

    /// <param name="outcome">completed, failed, or skipped-idempotent.</param>
    public void BatchFinished(string sourceKey, string outcome, double seconds)
    {
        var tags = new TagList
        {
            { DiagnosticNames.Tags.SourceKey, sourceKey },
            { "di.outcome", outcome },
        };

        _batches.Add(1, tags);
        _duration.Record(seconds, tags);
    }
}
