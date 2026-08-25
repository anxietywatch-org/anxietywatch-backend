using AnxietyWatch.Application.Features.Caregivers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnxietyWatch.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/caregiver")]
public sealed class CaregiverController(ISender sender) : ControllerBase
{
    [HttpGet("patients")]
    public async Task<ActionResult<IReadOnlyList<LinkedPatientResponse>>> Patients(
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetLinkedPatientsQuery(), cancellationToken));

    [HttpGet("patients/{patientId:guid}")]
    public async Task<ActionResult<PatientDetailResponse>> Patient(
        Guid patientId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPatientDetailQuery(patientId), cancellationToken));
}
