using DataIntegration.Core.Entities;
using DataIntegration.Core.Transformation;
using DataIntegration.Infrastructure.Transformation;
using Microsoft.AspNetCore.Mvc;

namespace DataIntegration.Api.Controllers;

/// <summary>
/// Inspect and replay rows that could not be transformed.
/// </summary>
/// <remarks>
/// A dead letter here is a work queue, not a graveyard: the raw payload was
/// preserved at ingest, so once the cause is fixed the row can be replayed
/// without going back to the upstream.
/// </remarks>
[ApiController]
[Route("api/v1/dead-letter")]
public class DeadLetterController(
    IDeadLetterStore store,
    DeadLetterReplayService replayService) : ControllerBase
{
    private const int MaxPageSize = 200;

    /// <summary>Lists dead letters, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DeadLetterView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] DeadLetterStatus? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Clamp(take, 1, MaxPageSize);

        var records = await store.ListAsync(
            status, Math.Max(skip, 0), page, cancellationToken);

        return Ok(records.Select(DeadLetterView.From).ToList());
    }

    /// <summary>
    /// Counts grouped by error code — the first thing worth knowing, because it
    /// says whether 400 failures are one problem or four.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(IReadOnlyDictionary<string, int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(
        [FromQuery] DeadLetterStatus? status,
        CancellationToken cancellationToken) =>
        Ok(await store.CountByErrorCodeAsync(status, cancellationToken));

    /// <summary>
    /// Re-transforms one dead-lettered row with the current mapping rules.
    /// </summary>
    /// <remarks>
    /// Returns 200 when the row is now canonical, 409 when it still fails — with
    /// the current reason, which may differ from the original one. A row that
    /// still fails stays pending and can be replayed again.
    /// </remarks>
    [HttpPost("{id:long}/replay")]
    [ProducesResponseType(typeof(ReplayResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Replay(long id, CancellationToken cancellationToken)
    {
        var outcome = await replayService.ReplayAsync(id, cancellationToken);

        return outcome.Status switch
        {
            ReplayStatus.NotFound => Problem(
                title: "Dead letter not found",
                detail: $"No dead letter with id {id}.",
                statusCode: StatusCodes.Status404NotFound),

            ReplayStatus.AlreadyReplayed => Ok(
                new ReplayResponse(id, "already-replayed", null, null)),

            ReplayStatus.Replayed => Ok(
                new ReplayResponse(id, "replayed", null, null)),

            _ => Problem(
                title: "Replay still failing",
                detail: outcome.Message,
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = outcome.ErrorCode,
                    ["deadLetterId"] = id,
                }),
        };
    }
}

public sealed record DeadLetterView(
    long Id,
    Guid IngestBatchId,
    string SourceKey,
    string ErrorCode,
    string ErrorMessage,
    DateTime OccurredAtUtc,
    DeadLetterStatus Status,
    int ReplayCount,
    string RawRowPreview)
{
    /// <summary>Payloads can be large, so the list view shows only a preview.</summary>
    private const int PreviewLength = 300;

    public static DeadLetterView From(DeadLetterRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new DeadLetterView(
            record.Id,
            record.IngestBatchId,
            record.SourceKey,
            record.ErrorCode,
            record.ErrorMessage,
            record.OccurredAtUtc,
            record.Status,
            record.ReplayCount,
            record.RawRow.Length <= PreviewLength
                ? record.RawRow
                : record.RawRow[..PreviewLength] + "…");
    }
}

public sealed record ReplayResponse(
    long DeadLetterId,
    string Outcome,
    string? ErrorCode,
    string? Message);
