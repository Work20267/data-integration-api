using DataIntegration.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataIntegration.Infrastructure.Persistence.Configurations;

public class StagingRecordConfiguration : IEntityTypeConfiguration<StagingRecord>
{
    public void Configure(EntityTypeBuilder<StagingRecord> builder)
    {
        builder.ToTable("StagingRecord");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceKey).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RawPayload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.SensorModel).HasMaxLength(50);
        builder.Property(x => x.ReceivedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(x => x.Status).HasConversion<int>();

        builder.HasOne(x => x.IngestBatch)
            .WithMany(x => x.StagingRecords)
            .HasForeignKey(x => x.IngestBatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // The transform worker scans for pending rows within a batch.
        builder.HasIndex(x => new { x.IngestBatchId, x.Status });
    }
}
