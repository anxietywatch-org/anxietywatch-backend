using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Caregivers;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using MediatR;

namespace AnxietyWatch.Application.Features.Caregivers;

public sealed record UnlinkCaregiverPatientCommand(Guid PatientId) : IRequest;

public sealed class UnlinkCaregiverPatientCommandHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    ICaregiverPatientLinkRepository links,
    ILinkTokenRepository tokens)
    : IRequestHandler<UnlinkCaregiverPatientCommand>
{
    public async Task Handle(UnlinkCaregiverPatientCommand request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }

        var caregiver = await users.GetByIdAsync(currentUser.UserId, cancellationToken)
            ?? throw new UnauthorizedApplicationException("The current account no longer exists.");
        if (!string.Equals(caregiver.Role, "family_member", StringComparison.Ordinal))
        {
            throw new ForbiddenException("Only caregiver accounts can unlink a patient.");
        }

        await links.RemoveLinkAsync(currentUser.UserId, request.PatientId, cancellationToken);
        await tokens.RevokeAcceptedCaregiverRelationshipsAsync(request.PatientId, currentUser.UserId, cancellationToken);
    }
}
