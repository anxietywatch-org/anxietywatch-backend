using System.Security.Cryptography;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.FamilyPlans;
using AnxietyWatch.Domain.Caregivers;
using AnxietyWatch.Domain.Users;
using MediatR;

namespace AnxietyWatch.Application.Features.Caregivers;

public sealed record CreateCaregiverInvitationCommand(Guid PatientId) : IRequest<CreateCaregiverInvitationResponse>;
public sealed record CreateCaregiverInvitationResponse(string Code, DateTimeOffset ExpiresAt);
public sealed record AcceptCaregiverInvitationRequest(string Code);
public sealed record AcceptCaregiverInvitationCommand(string Code) : IRequest<AcceptCaregiverInvitationResponse>;
public sealed record AcceptCaregiverInvitationResponse(Guid PatientId, DateTimeOffset LinkedAt);
public sealed record RevokeCaregiverInvitationCommand(Guid Id) : IRequest<bool>;

public sealed class CreateCaregiverInvitationHandler(ICurrentUser currentUser, IUserRepository users, IFamilyPlanPatientAuthorizer authorizer, ICaregiverInvitationRepository invitations, ISystemClock clock) : IRequestHandler<CreateCaregiverInvitationCommand, CreateCaregiverInvitationResponse>
{
    public async Task<CreateCaregiverInvitationResponse> Handle(CreateCaregiverInvitationCommand request, CancellationToken cancellationToken)
    {
        EnsureAuthenticated(currentUser);
        if (!await authorizer.CanManagePatientAsync(currentUser.UserId, request.PatientId, cancellationToken)) throw new ForbiddenException("The authenticated user cannot manage this patient.");
        if (await users.GetByIdAsync(request.PatientId, cancellationToken) is null) throw new NotFoundException("The patient was not found.");
        var invitation = new CaregiverInvitation(Guid.NewGuid(), currentUser.UserId, request.PatientId, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), clock.UtcNow.AddDays(30));
        await invitations.AddAsync(invitation, cancellationToken);
        return new(invitation.Code, invitation.ExpiresAt);
    }
    internal static void EnsureAuthenticated(ICurrentUser user) { if (!user.IsAuthenticated || user.UserId == Guid.Empty) throw new UnauthorizedApplicationException("Authentication is required."); }
}

public sealed class AcceptCaregiverInvitationHandler(ICurrentUser currentUser, IUserRepository users, ICaregiverInvitationRepository invitations, ICaregiverPatientLinkRepository links, ISystemClock clock) : IRequestHandler<AcceptCaregiverInvitationCommand, AcceptCaregiverInvitationResponse>
{
    public async Task<AcceptCaregiverInvitationResponse> Handle(AcceptCaregiverInvitationCommand request, CancellationToken cancellationToken)
    {
        CreateCaregiverInvitationHandler.EnsureAuthenticated(currentUser);
        if (string.IsNullOrWhiteSpace(request.Code)) throw new NotFoundException("The invitation code is invalid.");
        var invitation = await invitations.GetByCodeAsync(request.Code.Trim(), cancellationToken) ?? throw new NotFoundException("The invitation code is invalid.");
        if (invitation.TargetPatientId == currentUser.UserId) throw new ConflictException("A patient cannot accept an invitation to themselves.");
        if (await users.GetByIdAsync(invitation.TargetPatientId, cancellationToken) is null) throw new NotFoundException("The patient was not found.");
        var now = clock.UtcNow;
        if (invitation.ExpiresAt <= now || invitation.Status == CaregiverInvitationStatus.Deleted) throw new ConflictException("The invitation is no longer available.");
        if (invitation.Status == CaregiverInvitationStatus.Accepted && invitation.AcceptedByCaregiverId != currentUser.UserId) throw new ConflictException("The invitation has already been accepted.");
        var accepted = invitation.Status == CaregiverInvitationStatus.Accepted ? invitation : await invitations.TryAcceptAsync(invitation.Id, currentUser.UserId, now, cancellationToken);
        if (accepted is null)
        {
            var current = await invitations.GetByCodeAsync(request.Code.Trim(), cancellationToken);
            if (current?.Status != CaregiverInvitationStatus.Accepted || current.AcceptedByCaregiverId != currentUser.UserId) throw new ConflictException("The invitation has already been accepted.");
            accepted = current;
        }
        var link = await links.EnsureLinkAsync(currentUser.UserId, accepted.TargetPatientId, accepted.Id, now, cancellationToken);
        return new(link.PatientId, link.CreatedAt);
    }
}

public sealed class RevokeCaregiverInvitationHandler(ICurrentUser currentUser, ICaregiverInvitationRepository invitations) : IRequestHandler<RevokeCaregiverInvitationCommand, bool>
{
    public async Task<bool> Handle(RevokeCaregiverInvitationCommand request, CancellationToken cancellationToken) { CreateCaregiverInvitationHandler.EnsureAuthenticated(currentUser); return await invitations.TryDeleteAsync(request.Id, currentUser.UserId, cancellationToken); }
}
