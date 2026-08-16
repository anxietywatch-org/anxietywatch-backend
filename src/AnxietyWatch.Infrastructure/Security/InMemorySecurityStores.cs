using System.Collections.Concurrent;
using AnxietyWatch.Application.Abstractions.Security;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.Infrastructure.Security;

public sealed class InMemoryRevokedTokenStore : IRevokedTokenStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> revokedTokens = new();

    public Task RevokeAsync(string jwtId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        revokedTokens[jwtId] = expiresAt;
        return Task.CompletedTask;
    }

    public Task<bool> IsRevokedAsync(string jwtId, CancellationToken cancellationToken = default)
    {
        if (!revokedTokens.TryGetValue(jwtId, out var expiresAt))
        {
            return Task.FromResult(false);
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            revokedTokens.TryRemove(jwtId, out _);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }
}

public sealed class InMemoryPasswordResetTokenStore : IPasswordResetTokenStore
{
    private readonly ConcurrentDictionary<string, ResetToken> tokens = new();

    public Task StoreAsync(string tokenHash, Guid userId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        tokens[tokenHash] = new ResetToken(userId, expiresAt);
        return Task.CompletedTask;
    }

    public Task<Guid?> ConsumeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!tokens.TryRemove(tokenHash, out var token) || token.ExpiresAt <= now)
        {
            return Task.FromResult<Guid?>(null);
        }

        return Task.FromResult<Guid?>(token.UserId);
    }

    private sealed record ResetToken(Guid UserId, DateTimeOffset ExpiresAt);
}

public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string recipientEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Email queued for {Recipient} with subject {Subject}.", recipientEmail, subject);
        return Task.CompletedTask;
    }

    public Task SendHtmlAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default) =>
        SendAsync(recipientEmail, subject, htmlBody, cancellationToken);
}
