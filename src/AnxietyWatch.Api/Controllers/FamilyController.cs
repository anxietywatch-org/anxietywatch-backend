using AnxietyWatch.Application.Features.FamilyPlans;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnxietyWatch.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/family")]
public sealed class FamilyController(ISender sender) : ControllerBase
{
    [HttpGet("patients")]
    public async Task<ActionResult<IReadOnlyList<FamilyPlanPatientResponse>>> Patients(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetFamilyPlanPatientsQuery(), cancellationToken));
}
