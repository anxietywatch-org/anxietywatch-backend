using AnxietyWatch.Application.Features.Plans.Queries.GetPlans;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AnxietyWatch.Api.Controllers;

[ApiController]
[Route("api/plans")]
public sealed class PlansController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PlanDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<PlanDto>>> Get(CancellationToken cancellationToken)
    {
        var plans = await sender.Send(new GetPlansQuery(), cancellationToken);
        return Ok(plans);
    }
}
