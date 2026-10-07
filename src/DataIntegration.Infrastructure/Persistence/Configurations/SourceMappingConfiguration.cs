using DataIntegration.Core;
using DataIntegration.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataIntegration.Infrastructure.Persistence.Configurations;

public class SourceMappingConfiguration : IEntityTypeConfiguration<SourceMapping>
{
    public void Configure(EntityTypeBuilder<SourceMapping> builder)
    {
        builder.ToTable("SourceMapping");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceKey).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SensorModel).HasMaxLength(50);
        builder.Property(x => x.SourceField).HasMaxLength(100).IsRequired();
        builder.Property(x => x.MetricCode).HasMaxLength(40).IsRequired();

        // A source must not map the same field twice for the same model.
        builder.HasIndex(x => new { x.SourceKey, x.SensorModel, x.SourceField }).IsUnique();

        builder.HasData(Seed());
    }

    /// <summary>
    /// The initial rule set, shipped in the migration so configuration travels with
    /// the schema. Onboarding a third source is rows here, not a code change.
    /// </summary>
    private static SourceMapping[] Seed() =>
    [
        // ---- Source 1: live JSON feed -------------------------------------------
        // Measurements arrive as a sensordatavalues array of {value_type, value}
        // pairs, so these field names are value_type values, not JSON properties.
        New(1,  SourceKeys.JsonLive, "P0",                   MetricCodes.Pm1),
        New(2,  SourceKeys.JsonLive, "P1",                   MetricCodes.Pm10),
        New(3,  SourceKeys.JsonLive, "P2",                   MetricCodes.Pm25),
        New(4,  SourceKeys.JsonLive, "P4",                   MetricCodes.Pm4),
        New(5,  SourceKeys.JsonLive, "temperature",          MetricCodes.Temperature),
        New(6,  SourceKeys.JsonLive, "humidity",             MetricCodes.Humidity),
        New(7,  SourceKeys.JsonLive, "pressure",             MetricCodes.Pressure),
        New(8,  SourceKeys.JsonLive, "pressure_at_sealevel", MetricCodes.PressureSeaLevel),
        New(9,  SourceKeys.JsonLive, "noise_LAeq",           MetricCodes.NoiseLaeq),

        // ---- Source 2: daily CSV archive ----------------------------------------
        // Flat, semicolon-delimited columns that differ per sensor model. Note id 16
        // against id 8: the same measurement is spelled "pressure_sealevel" here and
        // "pressure_at_sealevel" in the JSON feed. That single disagreement is why
        // mapping lives in a table rather than in code.
        New(10, SourceKeys.CsvArchive, "P0",                MetricCodes.Pm1),
        New(11, SourceKeys.CsvArchive, "P1",                MetricCodes.Pm10),
        New(12, SourceKeys.CsvArchive, "P2",                MetricCodes.Pm25),
        New(13, SourceKeys.CsvArchive, "temperature",       MetricCodes.Temperature),
        New(14, SourceKeys.CsvArchive, "humidity",          MetricCodes.Humidity),
        New(15, SourceKeys.CsvArchive, "pressure",          MetricCodes.Pressure),
        New(16, SourceKeys.CsvArchive, "pressure_sealevel", MetricCodes.PressureSeaLevel),
        New(17, SourceKeys.CsvArchive, "altitude",          MetricCodes.Altitude),
        New(18, SourceKeys.CsvArchive, "noise_LAeq",        MetricCodes.NoiseLaeq),

        // ---- model-specific rule --------------------------------------------
        // Added in response to a real batch: 63 rows dead-lettered as
        // NO_MAPPED_MEASUREMENTS from a Geiger counter on the network. This is the
        // only rule scoped to one sensor model, because "counts_per_minute" means
        // something specific to this device and nothing on an air-quality sensor.
        // The other three fields it reports (counts, hv_pulses, sample_time_ms)
        // are instrument diagnostics, not observations, and stay unmapped.
        ModelSpecific(19, SourceKeys.JsonLive, "Radiation Si22G",
            "counts_per_minute", MetricCodes.RadiationCpm),
    ];

    /// <summary>A rule that applies to any model reporting the field.</summary>
    private static SourceMapping New(int id, string sourceKey, string field, string metric) =>
        ModelSpecific(id, sourceKey, null, field, metric);

    /// <summary>
    /// A rule scoped to one sensor model. Resolution prefers these over the
    /// model-agnostic rules above, so a device can override a shared field
    /// without duplicating every other rule for it.
    /// </summary>
    private static SourceMapping ModelSpecific(
        int id, string sourceKey, string? model, string field, string metric) => new()
    {
        Id = id,
        SourceKey = sourceKey,
        SensorModel = model,
        SourceField = field,
        MetricCode = metric,
        IsRequired = false,
        IsActive = true,
    };
}
