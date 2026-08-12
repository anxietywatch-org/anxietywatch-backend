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

        if (token.Status != TokenStatus.Pending || token.ExpiresAt <= clock.UtcNow)
        {
            throw new ConflictException("The code is expired or has already been used.");
        }

        User accountForSession;
        if (string.Equals(token.Role, "self", StringComparison.OrdinalIgnoreCase))
        {
            accountForSession = await users.GetByIdAsync(token.UserId, cancellationToken)
                ?? throw new NotFoundException("The token owner no longer exists.");
        }
        else
        {
            var accountId = Guid.NewGuid();
            accountForSession = new User(
                accountId,
                "Cuidador",
                $"caregiver+{accountId:N}@device.anxietywatch.internal",
                Guid.NewGuid().ToString("N"),
                "free");
            await users.AddAsync(accountForSession, cancellationToken);
        }

        token.Accept(accountForSession.Id, clock.UtcNow);
        await tokens.UpdateAsync(token, cancellationToken);

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
