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

    [HttpPost("patients/link")]
    public async Task<ActionResult<LinkAdditionalPatientResponse>> LinkPatient(
        LinkAdditionalPatientCommand command,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(command, cancellationToken));

    [HttpPost("patients/{patientId:guid}/invitations")]
    public async Task<ActionResult<CreateCaregiverInvitationResponse>> CreateInvitation(Guid patientId, CancellationToken cancellationToken) => StatusCode(StatusCodes.Status201Created, await sender.Send(new CreateCaregiverInvitationCommand(patientId), cancellationToken));

    [HttpPost("invitations/accept")]
    public async Task<ActionResult<AcceptCaregiverInvitationResponse>> AcceptInvitation(AcceptCaregiverInvitationRequest request, CancellationToken cancellationToken) => Ok(await sender.Send(new AcceptCaregiverInvitationCommand(request.Code), cancellationToken));

    [HttpDelete("invitations/{id:guid}")]
    public async Task<IActionResult> DeleteInvitation(Guid id, CancellationToken cancellationToken) => await sender.Send(new RevokeCaregiverInvitationCommand(id), cancellationToken) ? NoContent() : NotFound();

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

    [HttpGet("patients/{patientId:guid}/telemetry/latest")]
    [HttpGet("patients/{patientId:guid}/heart-rate/latest")]
    public async Task<ActionResult<CaregiverLatestHeartRateResponse>> LatestHeartRate(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCaregiverLatestHeartRateQuery(patientId), cancellationToken);
        return result is null ? NoContent() : Ok(result);
    }
}
