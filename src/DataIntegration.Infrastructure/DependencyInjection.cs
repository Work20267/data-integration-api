using DataIntegration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
}
