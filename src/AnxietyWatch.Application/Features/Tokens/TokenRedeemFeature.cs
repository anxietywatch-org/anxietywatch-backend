using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Authentication;
using AnxietyWatch.Domain.Tokens;
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
    ISystemClock clock)
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
        var accountId = isSelf ? token.UserId : Guid.NewGuid();
        if (!await tokens.TryAcceptAsync(token.Id, accountId, now, cancellationToken))
        {
            throw new ConflictException("The code has already been used.");
        }

        User accountForSession;
        if (isSelf)
        {
            accountForSession = await users.GetByIdAsync(token.UserId, cancellationToken)
                ?? throw new NotFoundException("The token owner no longer exists.");
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
            await users.AddAsync(accountForSession, cancellationToken);
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
