namespace DataIntegration.Core.Transformation;

/// <summary>
/// Supplies the active mapping rules for a source.
/// </summary>
/// <remarks>
/// Separate from the engine so rules can be cached. Transforming ~17,700 rows
/// must not mean 17,700 queries against a table that changes maybe monthly.
/// </remarks>
public interface IMappingRuleProvider
{
    Task<MappingRuleSet> GetAsync(string sourceKey, CancellationToken cancellationToken);

    /// <summary>Drops the cache, so a rule change takes effect without a restart.</summary>
    void Invalidate();
}
