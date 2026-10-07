using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DataIntegration.Infrastructure.Persistence;

/// <summary>
/// Lets the EF tooling build a context without starting the web application.
/// </summary>
/// <remarks>
/// EF prefers this over the startup project's host, which matters in two places:
/// generating a migration no longer requires ASPNETCORE_ENVIRONMENT to be set so
/// that a connection string resolves, and the CD pipeline can produce an
/// idempotent SQL script without any database being reachable.
///
/// Scaffolding a migration reads the model, never the server, so the connection
/// string below only has to parse — it is not connected to.
/// </remarks>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DataIntegrationDbContext>
{
    private const string DesignTimeFallback =
        "Server=localhost,1433;Initial Catalog=DataIntegration;User Id=sa;" +
        "Password=design-time-only;Encrypt=True;TrustServerCertificate=True;";

    public DataIntegrationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? DesignTimeFallback;

        var options = new DbContextOptionsBuilder<DataIntegrationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new DataIntegrationDbContext(options);
    }
}
