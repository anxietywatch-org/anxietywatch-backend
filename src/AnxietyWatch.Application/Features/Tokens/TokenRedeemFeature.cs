using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Authentication;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.FamilyPlans;
using AnxietyWatch.Domain.Users;
using FluentValidation;
using MediatR;

namespace AnxietyWatch.Application.Features.Tokens;

public sealed record TokenRedeemResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    string Role,
    UserResponse User);

public sealed record TokenRedeemCommand(string Code, string DeviceId) : IRequest<TokenRedeemResponse>;

public sealed class TokenRedeemCommandValidator : AbstractValidator<TokenRedeemCommand>
{
    public TokenRedeemCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty();
        RuleFor(command => command.DeviceId).NotEmpty().MaximumLength(200);
    }
}

public sealed class TokenRedeemCommandHandler(
    ILinkTokenRepository tokens,
    IUserRepository users,
    IJwtTokenService jwtTokenService,
    ISystemClock clock,
    IFamilyPlanPatientMembershipRepository memberships,
    ICurrentUser currentUser)
    : IRequestHandler<TokenRedeemCommand, TokenRedeemResponse>
{
    public async Task<TokenRedeemResponse> Handle(
        TokenRedeemCommand command,
        CancellationToken cancellationToken)
    {
        var normalizedCode = command.Code.Trim().ToUpperInvariant();
        var token = await tokens.GetByCodeAsync(normalizedCode, cancellationToken)
            ?? throw new NotFoundException("The code is invalid.");

        if (token.Status == TokenStatus.Accepted)
        {
            throw new ConflictException("The code has already been used.");
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

        var isSelf = string.Equals(token.Role, "self", StringComparison.OrdinalIgnoreCase);
        var isPatientInvitation = string.Equals(token.Role, "patient", StringComparison.OrdinalIgnoreCase);
        var isCaregiverInvitation = string.Equals(token.Role, "family_member", StringComparison.OrdinalIgnoreCase);
        User? owner = null;
        if (isPatientInvitation)
        {
            owner = await users.GetByIdAsync(token.UserId, cancellationToken);
        }
        var authenticatedCaregiver = isCaregiverInvitation && currentUser.IsAuthenticated && currentUser.UserId != Guid.Empty
            ? await users.GetByIdAsync(currentUser.UserId, cancellationToken)
            : null;
        if (isCaregiverInvitation && currentUser.IsAuthenticated &&
            (authenticatedCaregiver is null || !string.Equals(authenticatedCaregiver.Role, "family_member", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("A caregiver session is required to accept a caregiver invitation.");
        }

        // An anonymous non-self token starts one stable provisional caregiver account.
        // An already authenticated caregiver keeps that account identity instead of
        // switching to a second account derived from the invitation token.
        var accountId = isCaregiverInvitation && authenticatedCaregiver is not null
            ? authenticatedCaregiver.Id
            : isSelf ? token.UserId : token.Id;
        User accountForSession;
        if (isSelf)
        {
            accountForSession = await users.GetByIdAsync(accountId, cancellationToken)
                ?? throw new NotFoundException("The token owner no longer exists.");
        }
        else if (authenticatedCaregiver is not null)
        {
            accountForSession = authenticatedCaregiver;
        }
        else
        {
            accountForSession = new User(
                accountId,
                "Cuidador",
                $"caregiver+{accountId:N}@device.anxietywatch.internal",
                Guid.NewGuid().ToString("N"),
                "free",
                token.Role);
            var existingAccount = await users.GetByIdAsync(accountForSession.Id, cancellationToken);
            if (existingAccount is not null)
            {
                accountForSession = existingAccount;
            }
            else
            {
                await users.AddAsync(accountForSession, cancellationToken);
            }
        }

        if (!await tokens.TryAcceptAsync(token.Id, token.Code, accountId, now, cancellationToken))
        {
            throw new ConflictException("The code has already been used.");
        }

        if (!isSelf && isPatientInvitation && owner is not null &&
            string.Equals(owner.PlanId, "family", StringComparison.OrdinalIgnoreCase))
        {
            await memberships.EnsureMembershipAsync(
                owner.Id,
                accountForSession.Id,
                token.Id,
                now,
                cancellationToken);
        }

        var jwt = jwtTokenService.Create(
            accountForSession.Id,
            accountForSession.Email,
            accountForSession.PlanId,
            accountForSession.SecurityVersion);
        return new TokenRedeemResponse(
            jwt.AccessToken,
            jwt.ExpiresAt,
            token.Role,
            RegisterCommandHandler.ToResponse(accountForSession));
    }
}
