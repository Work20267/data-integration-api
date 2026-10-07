using DataIntegration.Core.Entities;
using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DataIntegration.Infrastructure.Transformation;

public class StagingReader(DataIntegrationDbContext context) : IStagingReader
{
    /// <summary>
    /// SQL Server allows 2,100 parameters per command, and MarkAsync passes ids as
    /// parameters, so updates are chunked well below that.
    /// </summary>
    private const int MarkChunkSize = 1_000;

    public async Task<IReadOnlyList<StagedRow>> ReadPendingPageAsync(
        Guid batchId,
        long afterId,
        int take,
        CancellationToken cancellationToken) =>
        await context.StagingRecords
            .AsNoTracking()
            .Where(s => s.IngestBatchId == batchId
                        && s.Status == StagingStatus.Pending
                        && s.Id > afterId)
            .OrderBy(s => s.Id)
            .Take(take)
            .Select(s => new StagedRow(s.Id, s.SourceKey, s.SensorModel, s.RawPayload))
            .ToListAsync(cancellationToken);

    public async Task MarkAsync(
        IReadOnlyList<long> ids,
        StagingStatus status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        foreach (var chunk in ids.Chunk(MarkChunkSize))
        {
            await context.StagingRecords
                .Where(s => chunk.Contains(s.Id))
                .ExecuteUpdateAsync(
                    set => set.SetProperty(s => s.Status, status),
                    cancellationToken);
        }
    }
}
