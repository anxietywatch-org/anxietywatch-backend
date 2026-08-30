using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;

namespace AnxietyWatch.Application.Features.Caregivers;

public interface ICaregiverAccessAuthorizer
{
    Task RequireCaregiverAccessAsync(Guid patientId, CancellationToken cancellationToken = default);
}

public sealed class CaregiverAccessAuthorizer(
    ICurrentUser currentUser,
    ICaregiverRelationshipResolver relationships) : ICaregiverAccessAuthorizer
{
    public async Task RequireCaregiverAccessAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }

        if (!await relationships.IsLinkedAsync(currentUser.UserId, patientId, cancellationToken))
        {
            throw new ForbiddenException("The authenticated caregiver cannot access this patient.");
        }
    }
}
