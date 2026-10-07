using DataIntegration.Core;
using DataIntegration.Core.Entities;
using DataIntegration.Core.Sync;
using DataIntegration.Core.Transformation;

namespace DataIntegration.UnitTests.Transformation;

internal sealed class FakeStagingReader(IReadOnlyList<StagedRow> rows) : IStagingReader
{
    public List<(long[] Ids, StagingStatus Status)> Marks { get; } = [];

    public Task<IReadOnlyList<StagedRow>> ReadPendingPageAsync(
        Guid batchId, long afterId, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StagedRow>>(
            rows.Where(r => r.Id > afterId).OrderBy(r => r.Id).Take(take).ToList());

    public Task MarkAsync(
        IReadOnlyList<long> ids, StagingStatus status, CancellationToken cancellationToken)
    {
        Marks.Add(([.. ids], status));
        return Task.CompletedTask;
    }
}

internal sealed class FakeCanonicalWriter : ICanonicalWriter
{
    public List<CanonicalReading> Written { get; } = [];

    public Task<int> UpsertAsync(
        IReadOnlyList<CanonicalReading> readings, CancellationToken cancellationToken)
    {
        Written.AddRange(readings);
        return Task.FromResult(readings.Count);
    }
}

internal sealed class FakeDeadLetterStore : IDeadLetterStore
{
    public List<DeadLetterEntry> Written { get; } = [];

    public Task WriteAsync(
        Guid batchId, IReadOnlyList<DeadLetterEntry> entries, CancellationToken cancellationToken)
    {
        Written.AddRange(entries);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DeadLetterRecord>> ListAsync(
        DeadLetterStatus? status, int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DeadLetterRecord>>([]);

    public Task<DeadLetterRecord?> GetAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult<DeadLetterRecord?>(null);

    public Task MarkReplayedAsync(long id, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyDictionary<string, int>> CountByErrorCodeAsync(
        DeadLetterStatus? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(
            Written.GroupBy(w => w.ErrorCode, StringComparer.Ordinal)
                   .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal));
}

internal sealed class FakeMappingRuleProvider(MappingRuleSet ruleSet) : IMappingRuleProvider
{
    public int Loads { get; private set; }

    public Task<MappingRuleSet> GetAsync(string sourceKey, CancellationToken cancellationToken)
    {
        Loads++;
        return Task.FromResult(ruleSet);
    }

    public void Invalidate() { }
}

/// <summary>
/// A parser that can be told to fail or throw on specific payloads, so the
/// service-level isolation guarantee can be tested without crafting input that
/// happens to break a real parser.
/// </summary>
internal sealed class ScriptedParser : IPayloadParser
{
    public string SourceKey => SourceKeys.JsonLive;

    /// <summary>Payloads that should make the parser throw an unexpected exception.</summary>
    public HashSet<string> ThrowOn { get; } = new(StringComparer.Ordinal);

    /// <summary>Payloads the parser should reject cleanly.</summary>
    public HashSet<string> FailOn { get; } = new(StringComparer.Ordinal);

    public ParseResult Parse(string payload)
    {
        if (ThrowOn.Contains(payload))
        {
            throw new InvalidOperationException($"scripted explosion for '{payload}'");
        }

        if (FailOn.Contains(payload))
        {
            return ParseResult.Failed(TransformErrorCodes.PayloadMalformed, "scripted failure");
        }

        return ParseResult.Success(new ParsedReading
        {
            SensorId = payload.GetHashCode(StringComparison.Ordinal) & 0x7FFF,
            SensorModel = "SDS011",
            ObservedAtUtc = new DateTime(2026, 10, 7, 6, 30, 35, DateTimeKind.Utc),
            Values = new Dictionary<string, string>(StringComparer.Ordinal) { ["P1"] = "10.0" },
        });
    }
}

internal sealed class FakeSyncWatermarkStore : ISyncWatermarkStore
{
    public List<string> Advanced { get; } = [];

    public Task<SyncWatermark?> GetAsync(string sourceKey, CancellationToken cancellationToken) =>
        Task.FromResult<SyncWatermark?>(null);

    public Task AdvanceAsync(string sourceKey, CancellationToken cancellationToken)
    {
        Advanced.Add(sourceKey);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SyncWatermark>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SyncWatermark>>([]);
}
