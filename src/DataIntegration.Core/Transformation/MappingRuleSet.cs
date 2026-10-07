namespace DataIntegration.Core.Transformation;

/// <summary>
/// The active rules for one source, indexed for lookup.
/// </summary>
/// <remarks>
/// Resolution is model-specific first, then the model-agnostic fallback. That
/// ordering is what lets a single sensor model override a shared field without
/// duplicating every other rule for it.
/// </remarks>
public sealed class MappingRuleSet
{
    private readonly Dictionary<(string Model, string Field), MappingRule> _byModel;
    private readonly Dictionary<string, MappingRule> _byField;
    private readonly List<MappingRule> _required;

    public MappingRuleSet(string sourceKey, IEnumerable<MappingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        SourceKey = sourceKey;

        _byModel = [];
        _byField = [];
        _required = [];

        foreach (var rule in rules)
        {
            if (rule.SensorModel is { Length: > 0 } model)
            {
                _byModel[(model, rule.SourceField)] = rule;
            }
            else
            {
                _byField[rule.SourceField] = rule;
            }

            if (rule.IsRequired)
            {
                _required.Add(rule);
            }
        }
    }

    public string SourceKey { get; }

    public int RuleCount => _byModel.Count + _byField.Count;

    /// <summary>
    /// Finds the rule for a field, preferring one scoped to this sensor model.
    /// Returns false when the field has no rule at all, which is not an error —
    /// sources carry fields we have no interest in.
    /// </summary>
    public bool TryResolve(string? sensorModel, string sourceField, out MappingRule? rule)
    {
        if (sensorModel is { Length: > 0 } model
            && _byModel.TryGetValue((model, sourceField), out var modelRule))
        {
            rule = modelRule;
            return true;
        }

        if (_byField.TryGetValue(sourceField, out var sharedRule))
        {
            rule = sharedRule;
            return true;
        }

        rule = null;
        return false;
    }

    /// <summary>Rules marked required that apply to this sensor model.</summary>
    public IEnumerable<MappingRule> RequiredFor(string? sensorModel) =>
        _required.Where(r =>
            r.SensorModel is null
            || string.Equals(r.SensorModel, sensorModel, StringComparison.OrdinalIgnoreCase));
}
