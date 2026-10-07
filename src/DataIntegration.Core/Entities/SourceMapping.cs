namespace DataIntegration.Core.Entities;

/// <summary>
/// One field-level mapping rule. These rows are the transformation layer's entire
/// configuration — onboarding a third source is inserts here, not a code change
/// and a redeploy.
/// </summary>
public class SourceMapping
{
    public int Id { get; set; }

    /// <summary>A value from <see cref="SourceKeys"/>.</summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>
    /// Restricts the rule to one sensor model. Null applies it to every model,
    /// which is the common case for shared fields like temperature.
    /// </summary>
    public string? SensorModel { get; set; }

    /// <summary>The field name exactly as the source spells it.</summary>
    public string SourceField { get; set; } = string.Empty;

    /// <summary>A value from <see cref="MetricCodes"/>.</summary>
    public string MetricCode { get; set; } = string.Empty;

    /// <summary>Rows missing a required field are dead-lettered rather than partially accepted.</summary>
    public bool IsRequired { get; set; }

    public bool IsActive { get; set; } = true;
}
