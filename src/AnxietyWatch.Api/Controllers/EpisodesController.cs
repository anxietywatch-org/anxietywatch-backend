using AnxietyWatch.Application.Features.Dashboard;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnxietyWatch.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/episodes")]
public sealed class EpisodesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EpisodeResponse>>> Get(
        [FromQuery] int range = 7,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetEpisodesQuery(range), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<EpisodeResponse>> Create(
        CreateEpisodeCommand command,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
