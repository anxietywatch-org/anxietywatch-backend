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

    [HttpGet("patients/{patientId:guid}/episodes")]
    public async Task<ActionResult<IReadOnlyList<CaregiverEpisodeResponse>>> Episodes(
        Guid patientId,
        [FromQuery] int range = 7,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetCaregiverPatientEpisodesQuery(patientId, range), cancellationToken));

    [HttpGet("patients/{patientId:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<CaregiverEventResponse>>> Events(
        Guid patientId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetCaregiverPatientEventsQuery(patientId, limit), cancellationToken));

    [HttpGet("patients/{patientId:guid}/heart-rate/latest")]
    public async Task<ActionResult<CaregiverLatestHeartRateResponse>> LatestHeartRate(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCaregiverLatestHeartRateQuery(patientId), cancellationToken);
        return result is null ? NoContent() : Ok(result);
    }
}
