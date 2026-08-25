namespace AnxietyWatch.Domain.Users;

public sealed record EmailVerificationTokenState(
    string? TokenHash,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? SentAt);

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
    Task<bool> UpdatePlanAsync(Guid id, string planId, CancellationToken cancellationToken = default);
    Task<bool> UpdatePasswordAsync(Guid id, string passwordHash, CancellationToken cancellationToken = default);
    Task<User?> TryActivateCaregiverAsync(
        Guid id,
        long expectedVersion,
        string expectedEmail,
        string email,
        string passwordHash,
        CancellationToken cancellationToken = default);
    Task<User?> RegisterFailedLoginAsync(
        Guid id,
        DateTimeOffset now,
        string expectedPasswordHash,
        CancellationToken cancellationToken = default);
    Task<User?> RegisterSuccessfulLoginAsync(
        Guid id,
        DateTimeOffset now,
        long expectedVersion,
        string expectedPasswordHash,
        CancellationToken cancellationToken = default);
    Task<EmailVerificationTokenState?> StoreEmailVerificationTokenAsync(
        Guid id,
        DateTimeOffset sentAt,
        string tokenHash,
        DateTimeOffset expiresAt,
        long expectedVersion,
        CancellationToken cancellationToken = default);
    Task<bool> ConfirmEmailAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task RollbackEmailVerificationTokenAsync(
        Guid id,
        string tokenHash,
        DateTimeOffset sentAt,
        EmailVerificationTokenState previousState,
        CancellationToken cancellationToken = default);
}
