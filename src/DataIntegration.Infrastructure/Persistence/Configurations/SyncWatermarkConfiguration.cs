using DataIntegration.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataIntegration.Infrastructure.Persistence.Configurations;

public class SyncWatermarkConfiguration : IEntityTypeConfiguration<SyncWatermark>
{
    public void Configure(EntityTypeBuilder<SyncWatermark> builder)
    {
        builder.ToTable("SyncWatermark");

        // One watermark per source, so the source key is the key.
        builder.HasKey(x => x.SourceKey);
        builder.Property(x => x.SourceKey).HasMaxLength(50);

        builder.Property(x => x.LastObservedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(x => x.LastExternalKey).HasMaxLength(120);
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
    }
}
