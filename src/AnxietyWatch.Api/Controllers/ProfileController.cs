using AnxietyWatch.Application.Features.Settings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnxietyWatch.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public sealed class ProfileController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProfileResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetProfileQuery(), cancellationToken));

    [HttpPatch]
    public async Task<ActionResult<ProfileResponse>> Update(
        UpdateProfileCommand command,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(command, cancellationToken));
}
