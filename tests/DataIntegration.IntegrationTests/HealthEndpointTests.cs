using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DataIntegration.IntegrationTests;

/// <summary>
/// Phase 1 gate: the deployment smoke test in cd.yml hits this same endpoint,
/// so a regression here fails the build before it can fail a deploy.
/// </summary>
public sealed class HealthEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Healthy",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
