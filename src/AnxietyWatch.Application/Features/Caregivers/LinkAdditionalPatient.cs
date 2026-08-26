using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using FluentValidation;
using MediatR;

namespace AnxietyWatch.Application.Features.Caregivers;

public sealed record LinkAdditionalPatientCommand(string Code) : IRequest<LinkAdditionalPatientResponse>;

public sealed record LinkAdditionalPatientResponse(
    Guid PatientId,
    string FullName,
    string? AvatarUrl,
    string Role,
    DateTimeOffset LinkedAt);

public sealed class LinkAdditionalPatientCommandValidator : AbstractValidator<LinkAdditionalPatientCommand>
{
    public LinkAdditionalPatientCommandValidator() =>
        RuleFor(command => command.Code).NotEmpty().MaximumLength(100);
}

public sealed class LinkAdditionalPatientCommandHandler(
    ICurrentUser currentUser,
    ILinkTokenRepository tokens,
    IUserRepository users,
    ISystemClock clock)
    : IRequestHandler<LinkAdditionalPatientCommand, LinkAdditionalPatientResponse>
{
    public async Task<LinkAdditionalPatientResponse> Handle(
        LinkAdditionalPatientCommand command,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }

        var caregiver = await users.GetByIdAsync(currentUser.UserId, cancellationToken)
            ?? throw new UnauthorizedApplicationException("The current account no longer exists.");
        if (!string.Equals(caregiver.Role, "family_member", StringComparison.Ordinal))
        {
            throw new ForbiddenException("Only caregiver accounts can link a patient.");
        }

        var normalizedCode = command.Code.Trim().ToUpperInvariant();
        var token = await tokens.GetByCodeAsync(normalizedCode, cancellationToken)
            ?? throw new NotFoundException("The code is invalid.");

        if (!string.Equals(token.Role, "family_member", StringComparison.Ordinal))
        {
            throw new ConflictException("The code is not eligible for caregiver linking.");
        }

        if (token.Status == TokenStatus.Accepted)
        {
            throw new ConflictException(token.AcceptedBy == currentUser.UserId
                ? "This patient is already linked to the current account."
                : "The code has already been used.");
        }

        if (token.Status is TokenStatus.Deleted or TokenStatus.Expired)
        {
            throw new ConflictException("The code is no longer available.");
        }

        var now = clock.UtcNow;
        if (token.ExpiresAt <= now)
        {
            throw new GoneException("The code has expired.");
        }

        var patient = await users.GetByIdAsync(token.UserId, cancellationToken)
            ?? throw new NotFoundException("The invitation owner no longer exists.");
        if (!string.Equals(patient.Role, "patient", StringComparison.Ordinal))
        {
            throw new ConflictException("The code is not eligible for caregiver linking.");
        }

        if (!await tokens.TryAcceptAsync(token.Id, token.Code, currentUser.UserId, now, cancellationToken))
        {
            throw new ConflictException("The code has already been used.");
        }

        return new LinkAdditionalPatientResponse(
            patient.Id,
            patient.FullName,
            patient.AvatarUrl,
            token.Role,
            now);
    }
}
