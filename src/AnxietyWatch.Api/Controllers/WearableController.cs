using AnxietyWatch.Application.Features.Wearables;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnxietyWatch.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class WearableController(ISender sender) : ControllerBase
{
    [HttpPost("telemetry/batch")]
    public async Task<ActionResult<object>> SubmitTelemetry(
        TelemetryBatchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SubmitTelemetryBatchCommand(request), cancellationToken);
        return StatusCode(result.Accepted ? StatusCodes.Status202Accepted : StatusCodes.Status200OK,
            new { batchId = result.Id, result.Accepted, result.Duplicate });
    }

    [HttpPost("sos/trigger")]
    public async Task<ActionResult<object>> TriggerSos(
        SosTriggerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new TriggerSosCommand(request), cancellationToken);
        return StatusCode(result.Accepted ? StatusCodes.Status202Accepted : StatusCodes.Status200OK,
            new { eventId = result.Id, result.Accepted, result.Duplicate });
    }

    [HttpPost("sos/cancel")]
    public async Task<ActionResult<object>> CancelSos(
        SosCancelRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CancelSosCommand(request), cancellationToken);
        return StatusCode(result.Accepted ? StatusCodes.Status202Accepted : StatusCodes.Status200OK,
            new { eventId = result.Id, result.Accepted, result.Duplicate });
    }
}
