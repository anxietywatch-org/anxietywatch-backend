using AnxietyWatch.Application.Features.Devices;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnxietyWatch.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/devices")]
public sealed class DevicesController(ISender sender) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<DeviceResponse>> Register(
        RegisterDeviceCommand command,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(command, cancellationToken));

    [HttpPost("unregister")]
    public async Task<IActionResult> Unregister(
        UnregisterDeviceRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(new UnregisterDeviceCommand(request.Token), cancellationToken);
        return Ok(new { success = true });
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeviceResponse>>> Get(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetDevicesQuery(), cancellationToken));
}

public sealed record UnregisterDeviceRequest(string Token);