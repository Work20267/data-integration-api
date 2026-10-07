using DataIntegration.Core.Ingestion;
using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Ingestion;
using DataIntegration.Infrastructure.Observability;
using DataIntegration.Infrastructure.Persistence;
using DataIntegration.Infrastructure.Sources.SensorCommunity;
using DataIntegration.Infrastructure.Transformation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace DataIntegration.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence. The provider choice stays here so the API project
    /// never references a database driver directly — swapping SQL Server out would
    /// touch this file and nothing in the web layer.
    /// </summary>
    public static IServiceCollection AddDataIntegrationPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<DataIntegrationDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                // Azure SQL serverless returns transient errors while resuming from
                // an auto-pause, so retrying is normal operation here, not a net.
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
                sql.CommandTimeout(60);
            }));

        return services;
    }

    /// <summary>
    /// Registers the ingest pipeline and the upstream readers.
    /// </summary>
    /// <remarks>
    /// Options are bound by the host, which owns configuration; this method only
    /// wires services, so it stays usable from a test host that supplies options
    /// directly.
    /// </remarks>
    public static IServiceCollection AddDataIntegrationIngestion(this IServiceCollection services)
    {
        // Timeouts are owned by the resilience pipeline, not HttpClient. Leaving
        // HttpClient.Timeout at its default would cut an attempt short before the
        // pipeline's own timeout strategy could observe it.
        services.AddHttpClient<LiveFeedReader>(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;

                // Identifying the caller is basic courtesy to a volunteer-funded
                // upstream, and lets them contact us rather than just block us.
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "DataIntegration/0.3 (+https://github.com/Work20267/data-integration-api)");
            })
            .AddStandardResilienceHandler(resilience =>
            {
                // The live feed is ~8.5 MB, so a 10-second attempt timeout — the
                // default — would fail every call on a slow connection.
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(100);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(330);

                // Must be at least twice the attempt timeout or the options fail
                // validation at startup.
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(210);

                resilience.Retry.MaxRetryAttempts = 3;
                resilience.Retry.BackoffType = DelayBackoffType.Exponential;
                resilience.Retry.UseJitter = true;
            });

        services.AddTransient<IUpstreamReader>(sp => sp.GetRequiredService<LiveFeedReader>());

        services.AddScoped<IStagingWriter, StagingWriter>();
        services.AddScoped<IIngestBatchStore, IngestBatchStore>();
        services.AddScoped<IngestService>();

        // Instruments are process-wide; the meter must not be recreated per request.
        services.AddSingleton<IngestMetrics>();

        return services;
    }

    /// <summary>
    /// Registers the transformation pipeline: parsers, mapping rules, canonical
    /// writer and the dead-letter store.
    /// </summary>
    public static IServiceCollection AddDataIntegrationTransformation(this IServiceCollection services)
    {
        // One parser per source format. Phase 5 adds the CSV archive parser here
        // and nothing else changes — the mapping engine never sees a format.
        services.AddTransient<IPayloadParser, LiveFeedPayloadParser>();

        // Singleton so the rule cache survives across requests. Rules are read for
        // every one of ~17,700 rows in a batch.
        services.AddSingleton<IMappingRuleProvider, MappingRuleProvider>();

        services.AddScoped<IStagingReader, StagingReader>();
        services.AddScoped<ICanonicalWriter, CanonicalWriter>();
        services.AddScoped<IDeadLetterStore, DeadLetterStore>();

        services.AddScoped<TransformService>();
        services.AddScoped<DeadLetterReplayService>();

        services.AddSingleton<TransformMetrics>();

        return services;
    }
}
