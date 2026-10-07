using DataIntegration.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataIntegration.Infrastructure.Persistence.Configurations;

public class IngestBatchConfiguration : IEntityTypeConfiguration<IngestBatch>
{
    public void Configure(EntityTypeBuilder<IngestBatch> builder)
    {
        builder.ToTable("IngestBatch");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceKey).HasMaxLength(50).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.StartedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(x => x.CompletedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(x => x.Status).HasConversion<int>();

        // Replaying a request with the same key must be rejected, not re-processed.
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.SourceKey, x.StartedAtUtc });
    }
}
