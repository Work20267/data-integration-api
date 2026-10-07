using DataIntegration.Core;
using DataIntegration.Core.Sync;
using DataIntegration.Infrastructure.Sync;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DataIntegration.Api.Controllers;

/// <summary>
/// Visibility into the scheduled sync.
/// </summary>
/// <remarks>
/// Read-only. Triggering a sync by hand is what the ingest and transform
/// endpoints are for; this answers "is it running, and how far has it got".
/// </remarks>
[ApiController]
[Route("api/v1/sync")]
public class SyncController(
    ISyncWatermarkStore watermarks,
    IOptions<SyncOptions> options) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType(typeof(SyncStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var marks = await watermarks.ListAsync(cancellationToken);

        return Ok(new SyncStatusResponse(
            settings.Enabled,
            settings.IntervalMinutes,
            settings.Sources.Count > 0 ? [.. settings.Sources] : new[] { SourceKeys.JsonLive },
            marks.Select(w => new WatermarkView(
                w.SourceKey, w.LastObservedAtUtc, w.LastExternalKey, w.UpdatedAtUtc)).ToList()));
    }
}

/// <param name="Enabled">
/// False by default. Background polling would exhaust the App Service free
/// tier's daily CPU allowance and is impolite to a volunteer-funded upstream, so
/// enabling it is a decision rather than a default.
/// </param>
public sealed record SyncStatusResponse(
    bool Enabled,
    int IntervalMinutes,
    IReadOnlyList<string> Sources,
    IReadOnlyList<WatermarkView> Watermarks);

/// <param name="LastObservedAtUtc">Latest observation this source has written to canonical.</param>
public sealed record WatermarkView(
    string SourceKey,
    DateTime? LastObservedAtUtc,
    string? LastExternalKey,
    DateTime UpdatedAtUtc);
