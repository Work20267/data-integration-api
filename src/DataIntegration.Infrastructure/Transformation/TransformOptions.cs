using System.ComponentModel.DataAnnotations;

namespace DataIntegration.Infrastructure.Transformation;

public class TransformOptions
{
    public const string SectionName = "Transform";

    /// <summary>
    /// Staging rows read, transformed and flushed per cycle. Also the size of the
    /// temp-table bulk copy in the canonical writer.
    /// </summary>
    [Range(100, 20_000)]
    public int ChunkSize { get; set; } = 2_000;
}
