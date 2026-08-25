using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Users;
using MediatR;

namespace AnxietyWatch.Application.Features.Caregivers;

public sealed record PatientDetailResponse(
    Guid PatientId,
    string FullName,
    string? AvatarUrl);

public sealed record GetPatientDetailQuery(Guid PatientId) : IRequest<PatientDetailResponse>;

public sealed class GetPatientDetailQueryHandler(
    ICaregiverAccessAuthorizer authorizer,
    IUserRepository users)
    : IRequestHandler<GetPatientDetailQuery, PatientDetailResponse>
{
    public async Task<PatientDetailResponse> Handle(
        GetPatientDetailQuery request,
        CancellationToken cancellationToken)
    {
        await authorizer.RequireCaregiverAccessAsync(request.PatientId, cancellationToken);

        var patient = await users.GetByIdAsync(request.PatientId, cancellationToken)
            ?? throw new NotFoundException("The patient was not found.");

        return new PatientDetailResponse(patient.Id, patient.FullName, patient.AvatarUrl);
    }
}
