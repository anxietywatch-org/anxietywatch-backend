using System.Collections.Concurrent;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Users;

namespace AnxietyWatch.Infrastructure.Security;

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<Guid, User> users = new();
    private readonly object loginGate = new();

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (loginGate)
        {
            return Task.FromResult(users.TryGetValue(id, out var user) ? Clone(user) : null);
        }
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        lock (loginGate)
        {
            var user = users.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.Email, email, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(user is null ? null : Clone(user));
        }
    }

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        lock (loginGate)
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
        lock (loginGate)
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

    public Task<bool> UpdatePasswordAsync(
        Guid id,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        lock (loginGate)
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
        CancellationToken cancellationToken = default)
    {
        lock (loginGate)
        {
            if (!users.TryGetValue(id, out var user)) return Task.FromResult<User?>(null);
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
        lock (loginGate)
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

    private static User Clone(User user) => User.Rehydrate(
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
        user.Version);
}
