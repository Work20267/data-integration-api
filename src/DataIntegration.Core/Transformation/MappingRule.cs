namespace DataIntegration.Core.Transformation;

/// <summary>
/// One field-mapping rule, as the engine needs it.
/// </summary>
/// <remarks>
/// A read-only projection of the <c>SourceMapping</c> entity rather than the
/// entity itself, so the engine stays free of persistence concerns and can be
/// unit-tested without a database.
/// </remarks>
/// <param name="SensorModel">Null means the rule applies to every model.</param>
public sealed record MappingRule(
    string SourceKey,
    string? SensorModel,
    string SourceField,
    string MetricCode,
    bool IsRequired);
