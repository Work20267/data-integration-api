using System.Globalization;
using DataIntegration.Core;
using DataIntegration.Core.Sync;
using DataIntegration.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataIntegration.Infrastructure.Sync;

/// <summary>
/// Drives <see cref="SyncCycle"/> on a schedule.
/// </summary>
/// <remarks>
/// <para>
/// This class owns timing and scoping, and nothing else. Every decision about
/// what a cycle does lives in <see cref="SyncCycle"/>.
/// </para>
/// <para>
/// There is no in-memory queue between ingest and transform, and that is a
/// choice rather than an omission — the staging table already is the queue, and
/// it is durable. See ADR 007.
/// </para>
/// </remarks>
public class SyncWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SyncOptions> options,
    TimeProvider timeProvider,
    ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            InfrastructureLog.SyncDisabled(logger);
            return;
        }

        var sources = settings.Sources.Count > 0
            ? settings.Sources.ToArray()
            : [SourceKeys.JsonLive];

        var interval = TimeSpan.FromMinutes(settings.IntervalMinutes);

        InfrastructureLog.SyncStarting(logger, sources.Length, settings.IntervalMinutes);

        // Delaying the first cycle keeps startup clear of the first real requests,
        // and stops a crash-looping instance from hammering the upstream.
        await Task.Delay(
            TimeSpan.FromSeconds(settings.StartupDelaySeconds), timeProvider, stoppingToken);

        using var timer = new PeriodicTimer(interval, timeProvider);

        do
        {
            foreach (var source in sources)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                await RunSourceAsync(source, stoppingToken);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunSourceAsync(string sourceKey, CancellationToken stoppingToken)
    {
        // A scope per source: ingest and transform are scoped because they hold a
        // DbContext, and one long-lived context across every cycle would grow its
        // change tracker without bound.
        await using var scope = scopeFactory.CreateAsyncScope();

        var cycle = scope.ServiceProvider.GetRequiredService<SyncCycle>();

        // Keyed to the minute so an overlapping trigger — a manual call, or a
        // second instance — is recognised as a replay instead of re-fetching.
        var idempotencyKey = string.Create(
            CultureInfo.InvariantCulture,
            $"sync:{sourceKey}:{timeProvider.GetUtcNow():yyyy-MM-ddTHH:mm}");

        var result = await cycle.RunAsync(sourceKey, idempotencyKey, stoppingToken);

        switch (result.Outcome)
        {
            case SyncOutcome.Completed:
                InfrastructureLog.SyncCycleCompleted(
                    logger, sourceKey, result.RowsStaged,
                    result.RowsCanonical, result.RowsDeadLettered);
                break;

            case SyncOutcome.SkippedIdempotent:
                InfrastructureLog.SyncCycleSkipped(logger, sourceKey);
                break;

            default:
                // Already logged with its exception inside the cycle. The loop
                // continues: the next tick retries, and a source that is down
                // does not stop the others.
                break;
        }
    }
}
