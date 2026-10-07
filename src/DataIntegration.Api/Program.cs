using Azure.Monitor.OpenTelemetry.AspNetCore;
using DataIntegration.Api;
using DataIntegration.Core.Observability;
using DataIntegration.Infrastructure;
using DataIntegration.Infrastructure.Ingestion;
using DataIntegration.Infrastructure.Sources.SensorCommunity;
using DataIntegration.Infrastructure.Persistence;
using DataIntegration.Infrastructure.Sync;
using DataIntegration.Infrastructure.Transformation;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Default is not configured. Locally this comes from " +
        "appsettings.Development.json or compose.yaml; in Azure it is an App " +
        "Service setting using the managed identity and carries no password.");

builder.Services.AddDataIntegrationPersistence(connectionString);

builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "sql", tags: ["ready"]);

// ---- ingestion ------------------------------------------------------------
// Options are bound here because the host owns configuration; Infrastructure
// only wires services. ValidateOnStart means a bad value fails the deployment
// rather than the first request that happens to hit it.
builder.Services.AddOptions<SensorCommunityOptions>()
    .Bind(builder.Configuration.GetSection(SensorCommunityOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<IngestOptions>()
    .Bind(builder.Configuration.GetSection(IngestOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<TransformOptions>()
    .Bind(builder.Configuration.GetSection(TransformOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SyncOptions>()
    .Bind(builder.Configuration.GetSection(SyncOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDataIntegrationIngestion();
builder.Services.AddDataIntegrationTransformation();
builder.Services.AddDataIntegrationSync();

// ---- rate limiting --------------------------------------------------------
// Only the expensive endpoints are limited. An ingest pulls ~8.5 MB from a
// volunteer-funded upstream, so protecting them protects someone else's
// infrastructure as much as ours. Health probes stay unlimited on purpose:
// throttling a liveness check would make App Service recycle a healthy app.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    limiter.AddFixedWindowLimiter(RateLimitPolicies.Expensive, window =>
    {
        window.PermitLimit = 5;
        window.Window = TimeSpan.FromMinutes(1);

        // No queue: a caller that is over the limit should be told immediately
        // rather than held open while an 8.5 MB download is already in flight.
        window.QueueLimit = 0;
    });
});

// ---- telemetry ------------------------------------------------------------
// OpenTelemetry does the instrumentation; Azure Monitor is only an exporter.
// Swapping backend later is a change here, not a re-instrumentation project.
var telemetry = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(DiagnosticNames.ActivitySource)
        // Statement text is not captured by default in this instrumentation, which
        // is what we want: ingested values must not leak into traces. Only the
        // operation, target and duration are exported.
        .AddSqlClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter(DiagnosticNames.Meter));

// Exporting is conditional: local runs and CI have no App Insights, and
// UseAzureMonitor without a connection string throws at startup.
var appInsights = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsights))
{
    telemetry.UseAzureMonitor(options => options.ConnectionString = appInsights);
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Local convenience only, so `docker compose up` yields a usable database
    // rather than an API talking to an empty one.
    //
    // Deliberately never in production: CD applies a reviewed, idempotent script
    // *before* the app deploys, so new code never starts against an old schema.
    // Migrating at startup would also mean several instances racing the same DDL.
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var database = scope.ServiceProvider
            .GetRequiredService<DataIntegrationDbContext>()
            .Database;

        await database.MigrateAsync();
    }
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

// Liveness carries no dependency checks on purpose. Azure SQL serverless
// auto-pauses after 60 minutes idle, and a /health that touched the database
// would report unhealthy during a resume — causing App Service to recycle an
// application that is in fact fine. The CD smoke test gates on this endpoint.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });

// Readiness is where dependencies are asserted: can we actually reach SQL.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.MapControllers();

app.Run();

#pragma warning disable CA1050 // Program must stay in the global namespace for WebApplicationFactory.
public partial class Program;
#pragma warning restore CA1050
