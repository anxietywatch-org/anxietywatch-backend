using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Tokens;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.Application.Features.Caregivers;

public interface ICaregiverAccessAuthorizer
{
    Task RequireCaregiverAccessAsync(Guid patientId, CancellationToken cancellationToken = default);
}

public sealed class CaregiverAccessAuthorizer(
    ICurrentUser currentUser,
    ILinkTokenRepository tokens,
    ILogger<CaregiverAccessAuthorizer> logger) : ICaregiverAccessAuthorizer
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
            logger.LogWarning(
                "Caregiver relationship authorization denied for patient {PatientId}, caregiver {CaregiverId}.",
                patientId, currentUser.UserId);
            throw new ForbiddenException("The authenticated caregiver cannot access this patient.");
        }
    }
}
