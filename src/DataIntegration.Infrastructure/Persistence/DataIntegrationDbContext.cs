using DataIntegration.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataIntegration.Infrastructure.Persistence;

/// <summary>
/// EF Core owns the configuration, metadata and dead-letter tables — low volume,
/// rich modelling, migrations. The high-throughput paths deliberately bypass it:
/// staging loads use SqlBulkCopy and canonical upserts use Dapper with MERGE,
/// neither of which EF can express efficiently.
/// </summary>
public class DataIntegrationDbContext(DbContextOptions<DataIntegrationDbContext> options)
    : DbContext(options)
{
    public DbSet<IngestBatch> IngestBatches => Set<IngestBatch>();
    public DbSet<StagingRecord> StagingRecords => Set<StagingRecord>();
    public DbSet<CanonicalReading> CanonicalReadings => Set<CanonicalReading>();
    public DbSet<CanonicalMeasurement> CanonicalMeasurements => Set<CanonicalMeasurement>();
    public DbSet<SourceMapping> SourceMappings => Set<SourceMapping>();
    public DbSet<DeadLetterRecord> DeadLetterRecords => Set<DeadLetterRecord>();
    public DbSet<SyncWatermark> SyncWatermarks => Set<SyncWatermark>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DataIntegrationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
