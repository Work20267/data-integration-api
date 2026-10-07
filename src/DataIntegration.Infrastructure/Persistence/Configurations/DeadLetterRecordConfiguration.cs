using DataIntegration.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataIntegration.Infrastructure.Persistence.Configurations;

public class DeadLetterRecordConfiguration : IEntityTypeConfiguration<DeadLetterRecord>
{
    public void Configure(EntityTypeBuilder<DeadLetterRecord> builder)
    {
        builder.ToTable("DeadLetterRecord");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceKey).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RawRow).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ErrorCode).HasMaxLength(60).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.OccurredAtUtc).HasColumnType("datetime2(3)");
        builder.Property(x => x.LastReplayAtUtc).HasColumnType("datetime2(3)");
        builder.Property(x => x.Status).HasConversion<int>();

        // The dead-letter endpoint lists pending rows; metrics group by error code.
        builder.HasIndex(x => new { x.Status, x.OccurredAtUtc });
        builder.HasIndex(x => x.ErrorCode);
        builder.HasIndex(x => x.IngestBatchId);
    }
}
