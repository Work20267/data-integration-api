using DataIntegration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace DataIntegration.IntegrationTests;

/// <summary>
/// A real SQL Server for the duration of the test run.
/// </summary>
/// <remarks>
/// The project's two hardest guarantees — that <c>MERGE</c> upserts rather than
/// duplicates, and that a mid-batch failure loses nothing — are properties of
/// SQL Server, not of C#. A mocked repository would assert them against my own
/// assumptions. This asserts them against the engine.
///
/// The container runs migrations on start, so the schema under test is the same
/// schema CD applies to Azure.
/// </remarks>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public DataIntegrationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DataIntegrationDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.CommandTimeout(180))
            .Options);

    /// <summary>Stops the container, severing every open connection.</summary>
    public Task StopAsync() => _container.StopAsync();

    public Task RestartAsync() => _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>
/// Shares one container across the classes that need it. Starting SQL Server
/// takes long enough that a container per class would dominate the run.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sqlserver";
}
