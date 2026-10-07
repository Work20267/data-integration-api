using DataIntegration.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataIntegration.Infrastructure.Persistence.Configurations;

public class CanonicalMeasurementConfiguration : IEntityTypeConfiguration<CanonicalMeasurement>
{
    public void Configure(EntityTypeBuilder<CanonicalMeasurement> builder)
    {
        builder.ToTable("CanonicalMeasurement");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.MetricCode).HasMaxLength(40).IsRequired();

        // Pressure runs to ~102543 Pa; particulate readings are fractional.
        builder.Property(x => x.Value).HasPrecision(18, 4);

        builder.HasOne(x => x.Reading)
            .WithMany(x => x.Measurements)
            .HasForeignKey(x => x.ReadingId)
            .OnDelete(DeleteBehavior.Cascade);

        // One value per metric per reading, so re-ingesting updates, never appends.
        builder.HasIndex(x => new { x.ReadingId, x.MetricCode }).IsUnique();
    }
}
