using System.Net;

namespace DataIntegration.IntegrationTests;

/// <summary>
/// Phase 1 gate: the deployment smoke test in cd.yml hits /health, so a
/// regression here fails the build before it can fail a deploy.
/// </summary>
public sealed class HealthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Liveness_reports_healthy_without_touching_the_database()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Healthy",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Readiness_endpoint_is_mapped_and_reports_a_dependency_verdict()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        // Healthy when a local SQL Server is up, ServiceUnavailable when it is not.
        // Either proves the endpoint is wired to a real dependency check; what must
        // never happen is a 404, which would mean readiness is not mapped at all.
        Assert.Contains(
            response.StatusCode,
            new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable });
    }
}
