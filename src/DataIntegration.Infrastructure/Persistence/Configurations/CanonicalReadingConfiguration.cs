using DataIntegration.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataIntegration.Infrastructure.Persistence.Configurations;

public class CanonicalReadingConfiguration : IEntityTypeConfiguration<CanonicalReading>
{
    public void Configure(EntityTypeBuilder<CanonicalReading> builder)
    {
        builder.ToTable("CanonicalReading");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExternalKey).HasMaxLength(120).IsRequired();
        builder.Property(x => x.SensorModel).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Country).HasMaxLength(10);
        builder.Property(x => x.LastSourceKey).HasMaxLength(50).IsRequired();

        // Coordinates arrive at wildly different precision (47.600 vs 53.58135997).
        builder.Property(x => x.Latitude).HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasPrecision(9, 6);

        builder.Property(x => x.ObservedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(x => x.FirstSeenAtUtc).HasColumnType("datetime2(3)");
        builder.Property(x => x.LastUpdatedAtUtc).HasColumnType("datetime2(3)");

        // The MERGE upsert targets this index. It is what makes ingesting the live
        // feed and the daily archive idempotent rather than duplicating every row.
        builder.HasIndex(x => x.ExternalKey).IsUnique();
        builder.HasIndex(x => new { x.SensorId, x.ObservedAtUtc });
        builder.HasIndex(x => x.ObservedAtUtc);
    }
}
