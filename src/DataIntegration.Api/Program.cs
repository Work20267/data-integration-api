var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Phase 1: liveness only. Phase 2 adds a SQL readiness check here.
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// The deployment smoke test in cd.yml gates on this endpoint.
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

#pragma warning disable CA1050 // Program must stay in the global namespace for WebApplicationFactory.
public partial class Program;
#pragma warning restore CA1050
