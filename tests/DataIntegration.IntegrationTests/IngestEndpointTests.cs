using System.Net;

namespace DataIntegration.IntegrationTests;

public sealed class IngestEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>
    /// Needs no database and no network: the source is resolved before either is
    /// touched, so this runs identically on a developer machine and on a CI runner
    /// with no SQL Server.
    /// </summary>
    [Fact]
    public async Task Unknown_source_returns_404_problem_details_not_500()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/v1/ingest/no-such-source", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // RFC 7807 ProblemDetails, so a client gets a machine-readable reason
        // rather than an opaque server error.
        Assert.Contains("Unknown source", body, StringComparison.Ordinal);
        Assert.Contains("no-such-source", body, StringComparison.Ordinal);
    }

    // A second test POSTing to the *registered* source was removed deliberately.
    // It triggered a real 8.5 MB fetch and a full ingest — 100 seconds — and then
    // passed or failed depending on the wall-clock minute, because the derived
    // idempotency key is per-minute. The test above already proves routing: a 404
    // produced by routing has no body, whereas this one asserts the handler's own
    // ProblemDetails. Exercising a real ingest belongs in the Phase 6 harness,
    // against a controlled upstream rather than the live network.
}
