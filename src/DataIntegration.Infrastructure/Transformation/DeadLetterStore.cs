using DataIntegration.Core.Entities;
using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DataIntegration.Infrastructure.Transformation;

/// <summary>
/// Dead letters through EF Core: low volume relative to canonical rows, and the
/// replay flow wants entity semantics. The EF side of ADR 002.
/// </summary>
public class DeadLetterStore(DataIntegrationDbContext context) : IDeadLetterStore
{
    /// <summary>Matches the ErrorMessage column width so a long message cannot fail the write.</summary>
    private const int MaxMessageLength = 2_000;

    public async Task WriteAsync(
        Guid batchId,
        IReadOnlyList<DeadLetterEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;

        context.DeadLetterRecords.AddRange(entries.Select(e => new DeadLetterRecord
        {
            IngestBatchId = batchId,
            SourceKey = e.SourceKey,
            RawRow = e.RawRow,
            ErrorCode = e.ErrorCode,
            ErrorMessage = e.ErrorMessage.Length <= MaxMessageLength
                ? e.ErrorMessage
                : e.ErrorMessage[..MaxMessageLength],
            OccurredAtUtc = now,
            Status = DeadLetterStatus.Pending,
        }));

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeadLetterRecord>> ListAsync(
        DeadLetterStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        await context.DeadLetterRecords
            .AsNoTracking()
            .Where(d => status == null || d.Status == status)
            .OrderByDescending(d => d.OccurredAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<DeadLetterRecord?> GetAsync(long id, CancellationToken cancellationToken) =>
        context.DeadLetterRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task MarkReplayedAsync(long id, CancellationToken cancellationToken) =>
        await context.DeadLetterRecords
            .Where(d => d.Id == id)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(d => d.Status, DeadLetterStatus.Replayed)
                    .SetProperty(d => d.ReplayCount, d => d.ReplayCount + 1)
                    .SetProperty(d => d.LastReplayAtUtc, DateTime.UtcNow),
                cancellationToken);

    public async Task<IReadOnlyDictionary<string, int>> CountByErrorCodeAsync(
        DeadLetterStatus? status,
        CancellationToken cancellationToken)
    {
        var counts = await context.DeadLetterRecords
            .AsNoTracking()
            .Where(d => status == null || d.Status == status)
            .GroupBy(d => d.ErrorCode)
            .Select(g => new { ErrorCode = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(c => c.ErrorCode, c => c.Count, StringComparer.Ordinal);
    }
}
