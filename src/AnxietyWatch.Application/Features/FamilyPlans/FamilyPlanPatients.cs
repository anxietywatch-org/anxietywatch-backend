using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.FamilyPlans;
using AnxietyWatch.Domain.Users;
using MediatR;

namespace AnxietyWatch.Application.Features.FamilyPlans;

public sealed record FamilyPlanPatientResponse(string PatientId, string Name);
public sealed record GetFamilyPlanPatientsQuery : IRequest<IReadOnlyList<FamilyPlanPatientResponse>>;

public sealed class GetFamilyPlanPatientsQueryHandler(ICurrentUser currentUser, IUserRepository users, IFamilyPlanPatientMembershipRepository memberships) : IRequestHandler<GetFamilyPlanPatientsQuery, IReadOnlyList<FamilyPlanPatientResponse>>
{
    public async Task<IReadOnlyList<FamilyPlanPatientResponse>> Handle(GetFamilyPlanPatientsQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty) throw new UnauthorizedApplicationException("Authentication is required.");
        var owner = await users.GetByIdAsync(currentUser.UserId, cancellationToken) ?? throw new UnauthorizedApplicationException("The session is invalid.");
        if (!string.Equals(owner.PlanId, "family", StringComparison.OrdinalIgnoreCase)) throw new ForbiddenException("A family plan is required.");
        var result = new List<FamilyPlanPatientResponse>();
        foreach (var membership in await memberships.ListPatientsAsync(owner.Id, cancellationToken))
        {
            var patient = await users.GetByIdAsync(membership.PatientUserId, cancellationToken);
            if (patient is not null) result.Add(new FamilyPlanPatientResponse(patient.Id.ToString(), patient.FullName));
        }
        return result;
    }
}
