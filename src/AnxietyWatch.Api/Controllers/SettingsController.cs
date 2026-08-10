using AnxietyWatch.Application.Features.Settings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnxietyWatch.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/settings")]
public sealed class SettingsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SettingsResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSettingsQuery(), cancellationToken));

    [HttpPatch]
    public async Task<ActionResult<SettingsResponse>> Update(
        UpdateSettingsCommand command,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(command, cancellationToken));
}
