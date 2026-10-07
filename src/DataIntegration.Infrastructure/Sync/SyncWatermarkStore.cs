using DataIntegration.Core.Entities;
using DataIntegration.Core.Sync;
using DataIntegration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DataIntegration.Infrastructure.Sync;

public class SyncWatermarkStore(DataIntegrationDbContext context) : ISyncWatermarkStore
{
    public Task<SyncWatermark?> GetAsync(string sourceKey, CancellationToken cancellationToken) =>
        context.SyncWatermarks
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.SourceKey == sourceKey, cancellationToken);

    public async Task<IReadOnlyList<SyncWatermark>> ListAsync(CancellationToken cancellationToken) =>
        await context.SyncWatermarks
            .AsNoTracking()
            .OrderBy(w => w.SourceKey)
            .ToListAsync(cancellationToken);

    /// <remarks>
    /// Derived from the canonical model rather than tracked as the pipeline runs.
    /// An incrementally maintained counter can drift from reality after a partial
    /// failure; a value read back from what was actually committed cannot.
    /// </remarks>
    public async Task AdvanceAsync(string sourceKey, CancellationToken cancellationToken)
    {
        var latest = await context.CanonicalReadings
            .AsNoTracking()
            .Where(r => r.LastSourceKey == sourceKey)
            .OrderByDescending(r => r.ObservedAtUtc)
            .ThenByDescending(r => r.ExternalKey)
            .Select(r => new { r.ObservedAtUtc, r.ExternalKey })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return;
        }

        var existing = await context.SyncWatermarks
            .FirstOrDefaultAsync(w => w.SourceKey == sourceKey, cancellationToken);

        if (existing is null)
        {
            context.SyncWatermarks.Add(new SyncWatermark
            {
                SourceKey = sourceKey,
                LastObservedAtUtc = latest.ObservedAtUtc,
                LastExternalKey = latest.ExternalKey,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }
        else
        {
            // Never move backwards. A backfill of older data must not make the
            // watermark claim the source is less complete than it is.
            if (existing.LastObservedAtUtc is null
                || latest.ObservedAtUtc > existing.LastObservedAtUtc)
            {
                existing.LastObservedAtUtc = latest.ObservedAtUtc;
                existing.LastExternalKey = latest.ExternalKey;
            }

            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
