using System.Collections.Concurrent;
using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DataIntegration.Infrastructure.Transformation;

/// <summary>
/// Loads mapping rules from the database and caches them per source.
/// </summary>
/// <remarks>
/// A singleton with its own cache, resolving a scoped DbContext per load.
/// Transforming ~17,700 rows must not mean 17,700 queries against a table that
/// changes perhaps monthly — and the rules are read on the hot path for every
/// single row.
/// </remarks>
public sealed class MappingRuleProvider(IServiceScopeFactory scopeFactory) : IMappingRuleProvider
{
    private readonly ConcurrentDictionary<string, MappingRuleSet> _cache =
        new(StringComparer.Ordinal);

    public async Task<MappingRuleSet> GetAsync(string sourceKey, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(sourceKey, out var cached))
        {
            return cached;
        }

        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataIntegrationDbContext>();

        var rules = await context.SourceMappings
            .AsNoTracking()
            .Where(m => m.SourceKey == sourceKey && m.IsActive)
            .Select(m => new MappingRule(
                m.SourceKey, m.SensorModel, m.SourceField, m.MetricCode, m.IsRequired))
            .ToListAsync(cancellationToken);

        var ruleSet = new MappingRuleSet(sourceKey, rules);

        // A concurrent miss just builds the set twice, which is harmless — the
        // alternative is holding a lock across a database call.
        _cache[sourceKey] = ruleSet;

        return ruleSet;
    }

    public void Invalidate() => _cache.Clear();
}
