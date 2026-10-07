using DataIntegration.Core.Entities;

namespace DataIntegration.Core.Sync;

/// <summary>
/// Records how far each source has been consumed.
/// </summary>
/// <remarks>
/// The watermark answers "where did we get to", so an interrupted or restarted
/// sync resumes rather than restarting the whole history. It is derived from the
/// canonical model rather than tracked incrementally, so it cannot drift away
/// from what was actually written.
/// </remarks>
public interface ISyncWatermarkStore
{
    Task<SyncWatermark?> GetAsync(string sourceKey, CancellationToken cancellationToken);

    /// <summary>
    /// Advances the watermark to the latest observation this source has written
    /// into the canonical model.
    /// </summary>
    Task AdvanceAsync(string sourceKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncWatermark>> ListAsync(CancellationToken cancellationToken);
}
