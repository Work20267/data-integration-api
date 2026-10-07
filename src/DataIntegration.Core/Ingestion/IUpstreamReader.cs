namespace DataIntegration.Core.Ingestion;

/// <summary>
/// Reads raw rows from one upstream source. Implementations are registered per
/// source and selected by <see cref="SourceKey"/>, so adding a third source means
/// adding an implementation rather than editing the ingest pipeline.
/// </summary>
public interface IUpstreamReader
{
    /// <summary>A value from <see cref="SourceKeys"/>.</summary>
    string SourceKey { get; }

    /// <summary>
    /// Streams rows rather than returning a list. The live feed is ~8.5 MB and
    /// ~17,700 records per call; materialising all of it before writing anything
    /// would hold the whole response in memory for no benefit.
    /// </summary>
    IAsyncEnumerable<RawRow> ReadAsync(CancellationToken cancellationToken);
}
