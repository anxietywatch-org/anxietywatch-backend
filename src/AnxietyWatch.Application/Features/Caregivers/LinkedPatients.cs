using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using MediatR;

namespace AnxietyWatch.Application.Features.Caregivers;

public sealed record LinkedPatientResponse(
    string PatientId,
    string FullName,
    string? AvatarUrl,
    string Role,
    DateTimeOffset LinkedAt);

public sealed record GetLinkedPatientsQuery : IRequest<IReadOnlyList<LinkedPatientResponse>>;

public sealed class GetLinkedPatientsQueryHandler(
    ICurrentUser currentUser,
    ILinkTokenRepository tokens,
    IUserRepository users)
    : IRequestHandler<GetLinkedPatientsQuery, IReadOnlyList<LinkedPatientResponse>>
{
    public async Task<IReadOnlyList<LinkedPatientResponse>> Handle(
        GetLinkedPatientsQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }

        var relationships = await tokens.GetAcceptedCaregiverRelationshipsAsync(currentUser.UserId, cancellationToken);
        var result = new List<LinkedPatientResponse>(relationships.Count);
        foreach (var relationship in relationships)
        {
            var patient = await users.GetByIdAsync(relationship.PatientId, cancellationToken);
            if (patient is null)
            {
                continue;
            }

            result.Add(new LinkedPatientResponse(
                patient.Id.ToString(),
                patient.FullName,
                patient.AvatarUrl,
                relationship.Role,
                relationship.LinkedAt));
        }

        return result;
    }
}
