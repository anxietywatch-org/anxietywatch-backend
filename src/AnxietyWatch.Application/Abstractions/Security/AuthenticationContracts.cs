using System.Security.Claims;

namespace AnxietyWatch.Application.Abstractions.Security;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

public interface IJwtTokenService
{
    JwtToken Create(Guid userId, string email, string planId);
}

public sealed record JwtToken(string AccessToken, DateTimeOffset ExpiresAt, string JwtId);

public interface ICurrentUser
{
    Guid UserId { get; }
    string? Email { get; }
    string? PlanId { get; }
    string? JwtId { get; }
    DateTimeOffset? TokenExpiresAt { get; }
    bool IsAuthenticated { get; }
}

public interface IRevokedTokenStore
{
    Task RevokeAsync(string jwtId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
    Task<bool> IsRevokedAsync(string jwtId, CancellationToken cancellationToken = default);
}

public interface IPasswordResetTokenStore
{
    Task StoreAsync(string tokenHash, Guid userId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
    Task<Guid?> ConsumeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default);
}

public interface IEmailSender
{
    Task SendAsync(string recipientEmail, string subject, string body, CancellationToken cancellationToken = default);
}
