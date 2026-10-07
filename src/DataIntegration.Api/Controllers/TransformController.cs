using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Transformation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataIntegration.Api.Controllers;

[ApiController]
[Route("api/v1/transform")]
[EnableRateLimiting(RateLimitPolicies.Expensive)]
public class TransformController(
    TransformService transformService,
    IMappingRuleProvider mappingRules) : ControllerBase
{
    /// <summary>
    /// Transforms a batch's pending staged rows into canonical readings.
    /// </summary>
    /// <remarks>
    /// Safe to call repeatedly. Rows are marked as they succeed or fail, so a
    /// second call only picks up what is still pending — and the canonical write
    /// is an upsert, so even a re-transformed row cannot duplicate.
    /// </remarks>
    [HttpPost("{batchId:guid}")]
    [ProducesResponseType(typeof(TransformResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Transform(Guid batchId, CancellationToken cancellationToken)
    {
        var summary = await transformService.TransformBatchAsync(batchId, cancellationToken);

        return Ok(new TransformResponse(
            summary.BatchId,
            summary.RowsRead,
            summary.RowsCanonical,
            summary.RowsDeadLettered,
            Math.Round(summary.Duration.TotalSeconds, 2)));
    }

    /// <summary>
    /// Drops the mapping-rule cache.
    /// </summary>
    /// <remarks>
    /// The rules are cached because they are read once per row. After inserting a
    /// rule — the usual fix for a NO_MAPPED_MEASUREMENTS dead letter — call this
    /// so a replay picks the new rule up without a restart.
    /// </remarks>
    [HttpPost("reload-mappings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult ReloadMappings()
    {
        mappingRules.Invalidate();
        return NoContent();
    }
}

/// <param name="RowsRead">Staged rows considered.</param>
/// <param name="RowsCanonical">Rows written to the canonical model.</param>
/// <param name="RowsDeadLettered">
/// Rows that could not be transformed. A non-zero value here alongside a large
/// <paramref name="RowsCanonical"/> is a success, not a failure — per-row
/// isolation means a batch is expected to have a partial outcome.
/// </param>
public sealed record TransformResponse(
    Guid BatchId,
    int RowsRead,
    int RowsCanonical,
    int RowsDeadLettered,
    double DurationSeconds);
