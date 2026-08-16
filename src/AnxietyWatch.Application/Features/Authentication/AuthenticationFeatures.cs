using System.Net;
using System.Security.Cryptography;
using System.Text;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Users;
using FluentValidation;
using MediatR;
using System.Text.Json.Serialization;

namespace AnxietyWatch.Application.Features.Authentication;

public sealed record UserResponse(
    string Id,
    string FullName,
    string Email,
    string PlanId,
    bool EmailVerified,
    string? AvatarUrl = null);

public sealed record AuthenticationResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    UserResponse User);

public sealed record RegisterCommand(
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("planId")] string PlanId,
    [property: JsonPropertyName("billingCycle")] string BillingCycle,
    [property: JsonPropertyName("paymentMethodToken")] string? PaymentMethodToken) : IRequest<AuthenticationResponse>;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(command => command.FullName).Length(2, 60).NotEmpty();
        RuleFor(command => command.Email).NotEmpty().EmailAddress();
        RuleFor(command => command.Password)
            .MinimumLength(8)
            .MaximumLength(30);
        RuleFor(command => command.PlanId)
            .Equal("free", StringComparer.OrdinalIgnoreCase)
            .WithMessage("Paid plans require a completed checkout.");
        RuleFor(command => command.BillingCycle)
            .Must(value => new[] { "monthly", "yearly" }
                .Contains(value, StringComparer.OrdinalIgnoreCase));
        RuleFor(command => command.PaymentMethodToken)
            .Must(string.IsNullOrWhiteSpace)
            .When(command => string.Equals(command.PlanId, "free", StringComparison.OrdinalIgnoreCase))
            .WithMessage("A free plan must not include a payment method token.");
    }
}

public sealed class RegisterCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    ISystemClock clock,
    IEmailSender emailSender,
    IEmailVerificationLinkFactory verificationLinkFactory)
    : IRequestHandler<RegisterCommand, AuthenticationResponse>
{
    public async Task<AuthenticationResponse> Handle(
        RegisterCommand command,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(command.Email);
        if (await users.GetByEmailAsync(email, cancellationToken) is not null)
        {
            throw new ConflictException("The email is already registered.");
        }

        var user = new User(
            Guid.NewGuid(),
            command.FullName.Trim(),
            email,
            passwordHasher.Hash(command.Password),
            "free");

        await users.AddAsync(user, cancellationToken);
        await TrySendInitialVerificationAsync(user, cancellationToken);
        return CreateResponse(user, jwtTokenService.Create(
            user.Id,
            user.Email,
            user.PlanId,
            user.SecurityVersion));
    }

    private async Task TrySendInitialVerificationAsync(User user, CancellationToken cancellationToken)
    {
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = ForgotPasswordCommandHandler.HashToken(rawToken);
        var sentAt = clock.UtcNow;
        var previousState = await users.StoreEmailVerificationTokenAsync(
            user.Id,
            sentAt,
            tokenHash,
            sentAt.AddHours(24),
            user.Version,
            cancellationToken);
        if (previousState is null) return;

        try
        {
            await emailSender.SendHtmlAsync(
                user.Email,
                "Verify your AnxietyWatch email",
                AnxietyWatchEmailTemplates.Verification(verificationLinkFactory.Create(rawToken), user.FullName),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (exception is EmailDeliveryException { DeliveryMayHaveSucceeded: true }) return;

            try
            {
                using var rollbackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await users.RollbackEmailVerificationTokenAsync(
                    user.Id,
                    tokenHash,
                    sentAt,
                    previousState,
                    rollbackTimeout.Token);
            }
            catch
            {
                // Registration remains valid. The user can request a fresh email from the pending screen.
            }
        }
    }

    internal static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    internal static AuthenticationResponse CreateResponse(User user, JwtToken token) =>
        new(token.AccessToken, token.ExpiresAt, ToResponse(user));

    internal static UserResponse ToResponse(User user) =>
        new(user.Id.ToString(), user.FullName, user.Email, user.PlanId, user.EmailVerified, user.AvatarUrl);
}

public sealed record LoginCommand(string Email, string Password) : IRequest<AuthenticationResponse>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(command => command.Email).NotEmpty().EmailAddress();
        RuleFor(command => command.Password).NotEmpty();
    }
}

public sealed class LoginCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    ISystemClock clock)
    : IRequestHandler<LoginCommand, AuthenticationResponse>
{
    public async Task<AuthenticationResponse> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var user = await users.GetByEmailAsync(RegisterCommandHandler.NormalizeEmail(command.Email), cancellationToken);

        if (user is null)
        {
            throw new UnauthorizedApplicationException("Invalid credentials.");
        }

        while (true)
        {
            if (user.IsLockedOut(now))
            {
                var retryAfter = Math.Max(1, (int)Math.Ceiling((user.LockoutUntil!.Value - now).TotalSeconds));
                throw new TooManyRequestsException("Too many failed login attempts.", retryAfter);
            }

            if (!passwordHasher.Verify(command.Password, user.PasswordHash))
            {
                var failedUser = await users.RegisterFailedLoginAsync(
                    user.Id,
                    now,
                    user.PasswordHash,
                    cancellationToken);
                if (failedUser is null)
                {
                    user = await users.GetByIdAsync(user.Id, cancellationToken)
                        ?? throw new UnauthorizedApplicationException("Invalid credentials.");
                    continue;
                }

                user = failedUser;

                if (user.IsLockedOut(now))
                {
                    throw new TooManyRequestsException("Too many failed login attempts.", 60);
                }

                throw new UnauthorizedApplicationException("Invalid credentials.");
            }

            var authenticatedUser = await users.RegisterSuccessfulLoginAsync(
                user.Id,
                now,
                user.Version,
                user.PasswordHash,
                cancellationToken);
            if (authenticatedUser is not null)
            {
                return RegisterCommandHandler.CreateResponse(
                    authenticatedUser,
                    jwtTokenService.Create(
                        authenticatedUser.Id,
                        authenticatedUser.Email,
                        authenticatedUser.PlanId,
                        authenticatedUser.SecurityVersion));
            }

            user = await users.GetByIdAsync(user.Id, cancellationToken)
                ?? throw new UnauthorizedApplicationException("Invalid credentials.");
        }
    }
}

public sealed record GetSessionQuery : IRequest<AuthenticationResponse>;

public sealed class GetSessionQueryHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    IJwtTokenService jwtTokenService)
    : IRequestHandler<GetSessionQuery, AuthenticationResponse>
{
    public async Task<AuthenticationResponse> Handle(
        GetSessionQuery request,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(currentUser, users, cancellationToken);
        return RegisterCommandHandler.CreateResponse(
            user,
            jwtTokenService.Create(user.Id, user.Email, user.PlanId, user.SecurityVersion));
    }

    internal static async Task<User> RequireUserAsync(
        ICurrentUser currentUser,
        IUserRepository users,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }

        return await users.GetByIdAsync(currentUser.UserId, cancellationToken)
            ?? throw new UnauthorizedApplicationException("The session is invalid.");
    }
}

public sealed record LogoutCommand : IRequest<bool>;

public sealed class LogoutCommandHandler(
    ICurrentUser currentUser,
    IRevokedTokenStore revokedTokens)
    : IRequestHandler<LogoutCommand, bool>
{
    public async Task<bool> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.JwtId) ||
            currentUser.TokenExpiresAt is null)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }

        await revokedTokens.RevokeAsync(currentUser.JwtId, currentUser.TokenExpiresAt.Value, cancellationToken);
        return true;
    }
}

public sealed record ForgotPasswordCommand(string Email) : IRequest<ForgotPasswordResponse>;

public sealed record ForgotPasswordResponse(string Message);

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator() => RuleFor(command => command.Email).NotEmpty().EmailAddress();
}

public sealed class ForgotPasswordCommandHandler(
    IPasswordRecoveryEmailQueue emailQueue)
    : IRequestHandler<ForgotPasswordCommand, ForgotPasswordResponse>
{
    private const string GenericMessage = "If the email exists, a recovery link will be sent.";

    public Task<ForgotPasswordResponse> Handle(
        ForgotPasswordCommand command,
        CancellationToken cancellationToken)
    {
        emailQueue.TryQueue(RegisterCommandHandler.NormalizeEmail(command.Email));

        return Task.FromResult(new ForgotPasswordResponse(GenericMessage));
    }

    internal static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed record ResetPasswordCommand(string Token, string NewPassword) : IRequest<string>;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(command => command.Token).NotEmpty();
        RuleFor(command => command.NewPassword).MinimumLength(8).MaximumLength(30);
    }
}

public sealed class ResetPasswordCommandHandler(
    IPasswordResetTokenStore resetTokens,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ISystemClock clock)
    : IRequestHandler<ResetPasswordCommand, string>
{
    public async Task<string> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var userId = await resetTokens.ConsumeAsync(
            ForgotPasswordCommandHandler.HashToken(command.Token),
            clock.UtcNow,
            cancellationToken);
        if (userId is null)
        {
            throw new GoneException("The recovery token is expired or has already been used.");
        }

        if (!await users.UpdatePasswordAsync(
                userId.Value,
                passwordHasher.Hash(command.NewPassword),
                cancellationToken))
        {
            throw new GoneException("The recovery token is invalid.");
        }
        return "Password updated";
    }
}

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<string>;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(command => command.CurrentPassword).NotEmpty();
        RuleFor(command => command.NewPassword).MinimumLength(8).MaximumLength(30);
    }
}

public sealed class ChangePasswordCommandHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender)
    : IRequestHandler<ChangePasswordCommand, string>
{
    public async Task<string> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await GetSessionQueryHandler.RequireUserAsync(currentUser, users, cancellationToken);
        if (!passwordHasher.Verify(command.CurrentPassword, user.PasswordHash))
        {
            throw new UnauthorizedApplicationException("The current password is invalid.");
        }

        user.UpdatePassword(passwordHasher.Hash(command.NewPassword));
        await users.UpdateAsync(user, cancellationToken);
        try
        {
            await emailSender.SendHtmlAsync(
                user.Email,
                "AnxietyWatch password changed",
                AnxietyWatchEmailTemplates.PasswordChanged(user.FullName),
                cancellationToken);
        }
        catch (EmailDeliveryException)
        {
            // The password update is committed; the notification is best-effort.
        }

        return "Password updated";
    }
}

public sealed record EmailVerificationStatusResponse(bool EmailVerified);

public sealed record GetEmailVerificationStatusQuery : IRequest<EmailVerificationStatusResponse>;

public sealed class GetEmailVerificationStatusQueryHandler(ICurrentUser currentUser, IUserRepository users)
    : IRequestHandler<GetEmailVerificationStatusQuery, EmailVerificationStatusResponse>
{
    public async Task<EmailVerificationStatusResponse> Handle(
        GetEmailVerificationStatusQuery request,
        CancellationToken cancellationToken)
    {
        var user = await GetSessionQueryHandler.RequireUserAsync(currentUser, users, cancellationToken);
        return new EmailVerificationStatusResponse(user.EmailVerified);
    }
}

public sealed record ResendVerificationEmailCommand : IRequest<string>;

public sealed class ResendVerificationEmailCommandHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    ISystemClock clock,
    IEmailSender emailSender,
    IEmailVerificationLinkFactory linkFactory)
    : IRequestHandler<ResendVerificationEmailCommand, string>
{
    public async Task<string> Handle(ResendVerificationEmailCommand request, CancellationToken cancellationToken)
    {
        User user;
        string rawToken;
        string tokenHash;
        DateTimeOffset sentAt;
        EmailVerificationTokenState previousState;
        while (true)
        {
            user = await GetSessionQueryHandler.RequireUserAsync(currentUser, users, cancellationToken);
            if (user.EmailVerified)
            {
                return "Email is already verified";
            }

            var now = clock.UtcNow;
            if (user.LastVerificationEmailSentAt is not null &&
                now - user.LastVerificationEmailSentAt < TimeSpan.FromSeconds(60))
            {
                throw new TooManyRequestsException("Verification email cooldown is active.", 60);
            }

            rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            tokenHash = ForgotPasswordCommandHandler.HashToken(rawToken);
            sentAt = now;
            var storedState = await users.StoreEmailVerificationTokenAsync(
                user.Id,
                sentAt,
                tokenHash,
                now.AddHours(24),
                user.Version,
                cancellationToken);
            if (storedState is not null)
            {
                previousState = storedState;
                break;
            }
        }

        try
        {
            var verificationLink = linkFactory.Create(rawToken);
            await emailSender.SendHtmlAsync(
                user.Email,
                "Verify your AnxietyWatch email",
                AnxietyWatchEmailTemplates.Verification(verificationLink, user.FullName),
                cancellationToken);
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                throw;
            }

            if (exception is not EmailDeliveryException { DeliveryMayHaveSucceeded: true })
            {
                try
                {
                    using var rollbackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await users.RollbackEmailVerificationTokenAsync(
                        user.Id,
                        tokenHash,
                        sentAt,
                        previousState,
                        rollbackTimeout.Token);
                }
                catch (Exception rollbackException)
                {
                    if (exception is EmailDeliveryException)
                    {
                        throw new ServiceUnavailableException(
                            "Email delivery is temporarily unavailable.",
                            innerException: new AggregateException(exception, rollbackException));
                    }

                    throw new AggregateException(exception, rollbackException);
                }
            }

            if (exception is EmailDeliveryException emailDeliveryException)
            {
                throw new ServiceUnavailableException(
                    "Email delivery is temporarily unavailable.",
                    emailDeliveryException.DeliveryMayHaveSucceeded ? 60 : 30,
                    innerException: emailDeliveryException);
            }

            throw;
        }

        return "Verification email sent";
    }

    internal static string CreateEmailBody(string verificationLink)
        => AnxietyWatchEmailTemplates.Verification(verificationLink, "usuario");
}

public sealed record ConfirmEmailCommand(string Token) : IRequest<string>;

public sealed class ConfirmEmailCommandValidator : AbstractValidator<ConfirmEmailCommand>
{
    public ConfirmEmailCommandValidator() => RuleFor(command => command.Token).NotEmpty().MaximumLength(256);
}

public sealed class ConfirmEmailCommandHandler(IUserRepository users, ISystemClock clock)
    : IRequestHandler<ConfirmEmailCommand, string>
{
    public async Task<string> Handle(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        var confirmed = await users.ConfirmEmailAsync(
            ForgotPasswordCommandHandler.HashToken(command.Token),
            clock.UtcNow,
            cancellationToken);
        if (!confirmed)
        {
            throw new GoneException("The verification link is expired or has already been used.");
        }

        return "Email verified";
    }
}
