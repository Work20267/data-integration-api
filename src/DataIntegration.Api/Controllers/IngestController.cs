using DataIntegration.Core.Ingestion;
using DataIntegration.Infrastructure.Ingestion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataIntegration.Api.Controllers;

/// <summary>
/// Ingest endpoints.
/// </summary>
/// <remarks>
/// The version is a literal route segment for now. Phase 7 replaces it with
/// Asp.Versioning so the version is negotiated rather than hardcoded.
/// </remarks>
[ApiController]
[Route("api/v1/ingest")]
[EnableRateLimiting(RateLimitPolicies.Expensive)]
public class IngestController(IngestService ingestService, ILogger<IngestController> logger)
    : ControllerBase
{
    /// <summary>
    /// Pulls the current window from a source and stages every row.
    /// </summary>
    /// <param name="source">A source key, e.g. <c>json-live</c>.</param>
    /// <param name="idempotencyKey">
    /// Optional. Supplying the same key twice returns the original batch instead
    /// of ingesting again. When omitted, a key is derived from the source and the
    /// current minute, which makes accidental rapid double-posts a no-op.
    /// </param>
    [HttpPost("{source}")]
    [ProducesResponseType(typeof(IngestResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(IngestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Ingest(
        string source,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"{source}:{DateTime.UtcNow:yyyy-MM-ddTHH:mm}"
            : idempotencyKey;

        try
        {
            var result = await ingestService.IngestAsync(source, key, cancellationToken);

            var body = new IngestResponse(
                result.BatchId,
                result.SourceKey,
                result.RowsStaged,
                result.WasAlreadyProcessed,
                Math.Round(result.Duration.TotalSeconds, 2));

            // 200 for a recognised replay, 202 for work actually done — the status
            // code alone tells the caller whether anything happened.
            return result.WasAlreadyProcessed
                ? Ok(body)
                : Accepted(body);
        }
        catch (UnknownSourceException ex)
        {
            // The caller asked for something that does not exist; not a server fault.
            return Problem(
                title: "Unknown source",
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Upstream {Source} was unreachable", source);

            // The upstream failed after the resilience pipeline exhausted its
            // retries. That is a gateway problem, not a bad request.
            return Problem(
                title: "Upstream unavailable",
                detail: $"Source '{source}' could not be reached after retries.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

/// <param name="RowsStaged">Raw rows written to staging. Transformation is Phase 4.</param>
/// <param name="WasAlreadyProcessed">True when the idempotency key was already seen.</param>
public sealed record IngestResponse(
    Guid BatchId,
    string SourceKey,
    int RowsStaged,
    bool WasAlreadyProcessed,
    double DurationSeconds);
