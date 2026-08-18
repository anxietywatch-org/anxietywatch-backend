using System.Collections.Concurrent;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Users;

namespace AnxietyWatch.Infrastructure.Security;

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<Guid, User> users = new();
    private readonly Dictionary<string, VerificationToken> verificationTokens = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(users.TryGetValue(id, out var user) ? Clone(user) : null);
        }
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var user = users.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.Email, email, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(user is null ? null : Clone(user));
        }
    }

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (users.Values.Any(candidate =>
                    string.Equals(candidate.Email, user.Email, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ConflictException("The email is already registered.");
            }

            if (!users.TryAdd(user.Id, Clone(user)))
            {
                throw new ConflictException("The user already exists.");
            }
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!users.TryGetValue(user.Id, out var current) || current.Version != user.Version)
            {
                throw new ConflictException("The user was modified by another request.");
            }

            var replacement = Clone(user);
            replacement.MarkPersisted();
            users[user.Id] = replacement;
            user.MarkPersisted();
            return Task.CompletedTask;
        }
    }

    public Task<bool> UpdatePlanAsync(Guid id, string planId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!users.TryGetValue(id, out var user)) return Task.FromResult(false);
            user.ChangePlan(planId);
            user.MarkPersisted();
            return Task.FromResult(true);
        }
    }

    public Task<bool> UpdatePasswordAsync(
        Guid id,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!users.TryGetValue(id, out var user)) return Task.FromResult(false);
            user.UpdatePassword(passwordHash);
            user.MarkPersisted();
            return Task.FromResult(true);
        }
    }

    public Task<User?> RegisterFailedLoginAsync(
        Guid id,
        DateTimeOffset now,
        string expectedPasswordHash,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!users.TryGetValue(id, out var user)) return Task.FromResult<User?>(null);
            if (user.PasswordHash != expectedPasswordHash) return Task.FromResult<User?>(null);
            user.RegisterFailedLogin(now);
            user.MarkPersisted();
            return Task.FromResult<User?>(Clone(user));
        }
    }

    public Task<User?> RegisterSuccessfulLoginAsync(
        Guid id,
        DateTimeOffset now,
        long expectedVersion,
        string expectedPasswordHash,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!users.TryGetValue(id, out var user)) return Task.FromResult<User?>(null);
            if (user.Version != expectedVersion ||
                user.PasswordHash != expectedPasswordHash ||
                user.IsLockedOut(now))
            {
                return Task.FromResult<User?>(null);
            }

            user.RegisterSuccessfulLogin();
            user.MarkPersisted();
            return Task.FromResult<User?>(Clone(user));
        }
    }

    public Task<EmailVerificationTokenState?> StoreEmailVerificationTokenAsync(
        Guid id,
        DateTimeOffset sentAt,
        string tokenHash,
        DateTimeOffset expiresAt,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!users.TryGetValue(id, out var user) ||
                user.EmailVerified ||
                user.Version != expectedVersion)
            {
                return Task.FromResult<EmailVerificationTokenState?>(null);
            }

            var previousToken = verificationTokens.FirstOrDefault(pair => pair.Value.UserId == id);
            var previousState = new EmailVerificationTokenState(
                previousToken.Key,
                previousToken.Key is null ? null : previousToken.Value.ExpiresAt,
                user.LastVerificationEmailSentAt);
            if (previousToken.Key is not null)
            {
                verificationTokens.Remove(previousToken.Key);
            }

            user.MarkVerificationEmailSent(sentAt);
            user.MarkPersisted();
            verificationTokens[tokenHash] = new VerificationToken(id, expiresAt);
            return Task.FromResult<EmailVerificationTokenState?>(previousState);
        }
    }

    public Task<bool> ConfirmEmailAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!verificationTokens.Remove(tokenHash, out var token) ||
                token.ExpiresAt <= now ||
                !users.TryGetValue(token.UserId, out var user) ||
                user.EmailVerified)
            {
                return Task.FromResult(false);
            }

            user.VerifyEmail();
            user.MarkPersisted();
            return Task.FromResult(true);
        }
    }

    public Task RollbackEmailVerificationTokenAsync(
        Guid id,
        string tokenHash,
        DateTimeOffset sentAt,
        EmailVerificationTokenState previousState,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (verificationTokens.TryGetValue(tokenHash, out var token) &&
                token.UserId == id &&
                users.TryGetValue(id, out var user) &&
                user.LastVerificationEmailSentAt == sentAt)
            {
                verificationTokens.Remove(tokenHash);
                if (previousState.TokenHash is not null && previousState.ExpiresAt is not null)
                {
                    verificationTokens[previousState.TokenHash] = new VerificationToken(
                        id,
                        previousState.ExpiresAt.Value);
                }

                user.RestoreVerificationEmailSentAt(previousState.SentAt);
                user.MarkPersisted();
            }

            return Task.CompletedTask;
        }
    }

    private static User Clone(User user) => User.Restore(
        user.Id,
        user.FullName,
        user.Email,
        user.PasswordHash,
        user.PlanId,
        user.EmailVerified,
        user.LastVerificationEmailSentAt,
        user.AvatarUrl,
        user.AnxietyThreshold,
        user.PushNotifications,
        user.PrivateMode,
        user.FailedLoginAttempts,
        user.FirstFailedLoginAt,
        user.LockoutUntil,
        user.Version,
        user.SecurityVersion,
        user.Role);

    private sealed record VerificationToken(Guid UserId, DateTimeOffset ExpiresAt);
}
