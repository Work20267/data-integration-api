using Microsoft.AspNetCore.Mvc.Testing;

namespace DataIntegration.IntegrationTests;

/// <summary>
/// Boots the API in-process for tests.
/// </summary>
/// <remarks>
/// The connection string is injected as an environment variable rather than through
/// ConfigureAppConfiguration: Program.cs reads configuration while the builder is
/// still being constructed, which happens before WebApplicationFactory's
/// configuration callbacks run, so anything added there arrives too late to be seen.
///
/// Phase 6 replaces this with a Testcontainers SQL Server instance for the tests
/// that genuinely exercise persistence. Liveness needs no database at all.
/// </remarks>
public class ApiFactory : WebApplicationFactory<Program>
{
    private const string TestConnectionString =
        "Server=localhost,1433;Initial Catalog=DataIntegration;User Id=sa;" +
        "Password=Local_Dev_P@ss1;Encrypt=True;TrustServerCertificate=True;" +
        "Connection Timeout=5;";

    public ApiFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", TestConnectionString);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
    }
}
