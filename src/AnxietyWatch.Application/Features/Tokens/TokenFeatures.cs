using System.Security.Cryptography;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Tokens;
using FluentValidation;
using MediatR;

namespace AnxietyWatch.Application.Features.Tokens;

public sealed record TokenResponse(
    string Id,
    string Code,
    string Role,
    DateTimeOffset ExpiresAt,
    string Status);

public sealed record TokenQuotaResponse(int Limit, int Used, int Remaining);

public sealed record CreateTokenCommand(string Role) : IRequest<TokenResponse>;

public sealed class CreateTokenCommandValidator : AbstractValidator<CreateTokenCommand>
{
    public CreateTokenCommandValidator() =>
        RuleFor(command => command.Role)
            .Must(value => new[] { "self", "family_member", "patient" }
                .Contains(value, StringComparer.OrdinalIgnoreCase));
}

public sealed class CreateTokenCommandHandler(
    ICurrentUser currentUser,
    ILinkTokenRepository tokens,
    ISystemClock clock)
    : IRequestHandler<CreateTokenCommand, TokenResponse>
{
    public async Task<TokenResponse> Handle(CreateTokenCommand command, CancellationToken cancellationToken)
    {
        RequireAuthenticatedUser(currentUser);
        var maximum = currentUser.PlanId?.ToLowerInvariant() switch
        {
            "free" or "individual" => 1,
            "family" => 5,
            "professional" => 20,
            _ => 0
        };
        if (maximum == 0)
        {
            throw new ForbiddenException("The current plan cannot create tokens.");
        }

        var token = new LinkToken(Guid.NewGuid(), currentUser.UserId, CreateCode(),
            command.Role.ToLowerInvariant(), clock.UtcNow.AddDays(30));
        if (!await tokens.TryAddAsync(token, maximum, cancellationToken))
        {
            throw new ConflictException("The token quota for the current plan has been reached.");
        }

        return Map(token);
    }

    private static string CreateCode()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        Span<char> segments = stackalloc char[12];
        var bytes = RandomNumberGenerator.GetBytes(segments.Length);
        for (var index = 0; index < segments.Length; index++)
        {
            segments[index] = alphabet[bytes[index] % alphabet.Length];
        }

        return $"AW-{new string(segments[..4])}-{new string(segments.Slice(4, 4))}-{new string(segments.Slice(8, 4))}";
    }

    internal static TokenResponse Map(LinkToken token) =>
        new(token.Id.ToString(), token.Code, token.Role, token.ExpiresAt, token.Status.ToString().ToLowerInvariant());

    internal static void RequireAuthenticatedUser(ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }
    }
}

public sealed record GetTokensQuery : IRequest<IReadOnlyList<TokenResponse>>;

public sealed record GetTokenQuotaQuery : IRequest<TokenQuotaResponse>;

public sealed class GetTokenQuotaQueryHandler(ICurrentUser currentUser, ILinkTokenRepository tokens)
    : IRequestHandler<GetTokenQuotaQuery, TokenQuotaResponse>
{
    public async Task<TokenQuotaResponse> Handle(GetTokenQuotaQuery request, CancellationToken cancellationToken)
    {
        CreateTokenCommandHandler.RequireAuthenticatedUser(currentUser);
        var limit = currentUser.PlanId?.ToLowerInvariant() switch
        {
            "free" or "individual" => 1,
            "family" => 5,
            "professional" => 20,
            _ => 0
        };
        var used = (await tokens.GetAsync(currentUser.UserId, cancellationToken))
            .Count(token => token.Status is not TokenStatus.Deleted);
        return new TokenQuotaResponse(limit, used, Math.Max(0, limit - used));
    }
}

public sealed class GetTokensQueryHandler(ICurrentUser currentUser, ILinkTokenRepository tokens)
    : IRequestHandler<GetTokensQuery, IReadOnlyList<TokenResponse>>
{
    public async Task<IReadOnlyList<TokenResponse>> Handle(GetTokensQuery request, CancellationToken cancellationToken)
    {
        CreateTokenCommandHandler.RequireAuthenticatedUser(currentUser);
        var result = await tokens.GetAsync(currentUser.UserId, cancellationToken);
        return result.Select(CreateTokenCommandHandler.Map).ToArray();
    }
}

public sealed record DeleteTokenCommand(Guid Id) : IRequest<bool>;

public sealed class DeleteTokenCommandHandler(ICurrentUser currentUser, ILinkTokenRepository tokens)
    : IRequestHandler<DeleteTokenCommand, bool>
{
    public async Task<bool> Handle(DeleteTokenCommand command, CancellationToken cancellationToken)
    {
        CreateTokenCommandHandler.RequireAuthenticatedUser(currentUser);
        var token = await tokens.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("Token not found.");
        if (token.UserId != currentUser.UserId)
        {
            throw new ForbiddenException("The token does not belong to the authenticated user.");
        }

        if (token.Status == TokenStatus.Accepted)
        {
            throw new ConflictException("An accepted token cannot be deleted.");
        }

        token.MarkDeleted();
        await tokens.UpdateAsync(token, cancellationToken);
        return true;
    }
}

public sealed record RevokeTokenCommand(Guid Id) : IRequest<bool>;

public sealed class RevokeTokenCommandHandler(ICurrentUser currentUser, ILinkTokenRepository tokens)
    : IRequestHandler<RevokeTokenCommand, bool>
{
    public async Task<bool> Handle(RevokeTokenCommand command, CancellationToken cancellationToken)
    {
        CreateTokenCommandHandler.RequireAuthenticatedUser(currentUser);
        var token = await tokens.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("Token not found.");
        if (token.UserId != currentUser.UserId)
        {
            throw new ForbiddenException("The token does not belong to the authenticated user.");
        }

        if (token.Status != TokenStatus.Accepted)
        {
            throw new ConflictException("Only an accepted token can be revoked.");
        }

        if (!await tokens.TryRevokeAsync(command.Id, cancellationToken))
        {
            throw new ConflictException("The token state changed before the request completed.");
        }

        return true;
    }
}

public sealed record AcceptTokenCommand(Guid Id, string DeviceId) : IRequest<string>;

public sealed class AcceptTokenCommandValidator : AbstractValidator<AcceptTokenCommand>
{
    public AcceptTokenCommandValidator() => RuleFor(command => command.DeviceId).NotEmpty().MaximumLength(200);
}

public sealed class AcceptTokenCommandHandler(
    ICurrentUser currentUser,
    ILinkTokenRepository tokens,
    ISystemClock clock)
    : IRequestHandler<AcceptTokenCommand, string>
{
    public async Task<string> Handle(AcceptTokenCommand command, CancellationToken cancellationToken)
    {
        CreateTokenCommandHandler.RequireAuthenticatedUser(currentUser);
        var token = await tokens.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("Token not found.");

        if (token.Status == TokenStatus.Accepted)
        {
            throw new ConflictException("The token has already been used.");
        }

        if (token.Status is TokenStatus.Deleted or TokenStatus.Expired)
        {
            throw new ConflictException("The token is no longer available.");
        }

        var now = clock.UtcNow;
        if (token.ExpiresAt <= now)
        {
            throw new GoneException("The token has expired.");
        }

        if (!await tokens.TryAcceptAsync(command.Id, currentUser.UserId, now, cancellationToken))
        {
            throw new ConflictException("The token has already been used.");
        }

        return "accepted";
    }
}

public sealed record ShareTokenCommand(Guid Id, string RecipientEmail) : IRequest<bool>;

public sealed class ShareTokenCommandValidator : AbstractValidator<ShareTokenCommand>
{
    public ShareTokenCommandValidator() => RuleFor(command => command.RecipientEmail).NotEmpty().EmailAddress();
}

public sealed class ShareTokenCommandHandler(
    ICurrentUser currentUser,
    ILinkTokenRepository tokens,
    IEmailSender emailSender)
    : IRequestHandler<ShareTokenCommand, bool>
{
    public async Task<bool> Handle(ShareTokenCommand command, CancellationToken cancellationToken)
    {
        CreateTokenCommandHandler.RequireAuthenticatedUser(currentUser);
        var token = await tokens.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("Token not found.");
        if (token.UserId != currentUser.UserId)
        {
            throw new ForbiddenException("The token does not belong to the authenticated user.");
        }

        await emailSender.SendHtmlAsync(
            command.RecipientEmail,
            "AnxietyWatch token invitation",
            AnxietyWatchEmailTemplates.TokenInvitation(token.Code),
            cancellationToken);
        return true;
    }
}
