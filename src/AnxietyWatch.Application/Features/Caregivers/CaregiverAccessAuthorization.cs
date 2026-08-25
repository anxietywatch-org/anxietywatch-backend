using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Tokens;

namespace AnxietyWatch.Application.Features.Caregivers;

public interface ICaregiverAccessAuthorizer
{
    Task RequireCaregiverAccessAsync(Guid patientId, CancellationToken cancellationToken = default);
}

public sealed class CaregiverAccessAuthorizer(
    ICurrentUser currentUser,
    ILinkTokenRepository tokens) : ICaregiverAccessAuthorizer
{
    public async Task RequireCaregiverAccessAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }

        if (!await tokens.HasAcceptedCaregiverRelationshipAsync(
                patientId,
                currentUser.UserId,
                cancellationToken))
        {
            throw new ForbiddenException("The authenticated caregiver cannot access this patient.");
        }
    }
}
