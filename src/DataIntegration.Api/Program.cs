using DataIntegration.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

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
