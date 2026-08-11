using AnxietyWatch.Domain.Common;

namespace AnxietyWatch.Domain.Users;

public sealed class User : AggregateRoot
{
    private User() : base(Guid.Empty)
    {
    }

    public User(Guid id, string fullName, string email, string passwordHash, string planId)
        : base(id)
    {
        FullName = fullName;
        Email = email;
        PasswordHash = passwordHash;
        PlanId = planId;
    }

    public static User Restore(
        Guid id,
        string fullName,
        string email,
        string passwordHash,
        string planId,
        bool emailVerified,
        DateTimeOffset? lastVerificationEmailSentAt,
        string? avatarUrl,
        int anxietyThreshold,
        bool pushNotifications,
        bool privateMode,
        int failedLoginAttempts,
        DateTimeOffset? firstFailedLoginAt,
        DateTimeOffset? lockoutUntil,
        long version)
    {
        var user = new User(id, fullName, email, passwordHash, planId)
        {
            EmailVerified = emailVerified,
            LastVerificationEmailSentAt = lastVerificationEmailSentAt,
            AvatarUrl = avatarUrl,
            AnxietyThreshold = anxietyThreshold,
            PushNotifications = pushNotifications,
            PrivateMode = privateMode,
            FailedLoginAttempts = failedLoginAttempts,
            FirstFailedLoginAt = firstFailedLoginAt,
            LockoutUntil = lockoutUntil,
            Version = version
        };

        return user;
    }

    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string PlanId { get; private set; } = string.Empty;
    public bool EmailVerified { get; private set; }
    public DateTimeOffset? LastVerificationEmailSentAt { get; private set; }
    public string? AvatarUrl { get; private set; }
    public int AnxietyThreshold { get; private set; } = 70;
    public bool PushNotifications { get; private set; } = true;
    public bool PrivateMode { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? FirstFailedLoginAt { get; private set; }
    public DateTimeOffset? LockoutUntil { get; private set; }
    public long Version { get; private set; }

    public void RegisterFailedLogin(DateTimeOffset now)
    {
        if (FirstFailedLoginAt is null || now - FirstFailedLoginAt > TimeSpan.FromMinutes(1))
        {
            FailedLoginAttempts = 0;
            FirstFailedLoginAt = now;
        }

        FailedLoginAttempts++;
        if (FailedLoginAttempts >= 5)
        {
            var candidate = now.AddSeconds(60);
            if (LockoutUntil is null || candidate > LockoutUntil)
            {
                LockoutUntil = candidate;
            }
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        FirstFailedLoginAt = null;
        LockoutUntil = null;
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutUntil > now;

    public void MarkVerificationEmailSent(DateTimeOffset now) => LastVerificationEmailSentAt = now;

    public void RestoreVerificationEmailSentAt(DateTimeOffset? sentAt) => LastVerificationEmailSentAt = sentAt;

    public void VerifyEmail() => EmailVerified = true;

    public void UpdatePassword(string passwordHash) => PasswordHash = passwordHash;

    public void UpdateProfile(string fullName, string? avatarUrl)
    {
        FullName = fullName;
        AvatarUrl = avatarUrl;
    }

    public void UpdateSettings(int anxietyThreshold, bool pushNotifications, bool privateMode)
    {
        AnxietyThreshold = anxietyThreshold;
        PushNotifications = pushNotifications;
        PrivateMode = privateMode;
    }

    public void MarkPersisted() => Version++;
}
