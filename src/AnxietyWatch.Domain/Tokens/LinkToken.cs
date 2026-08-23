using AnxietyWatch.Domain.Common;

namespace AnxietyWatch.Domain.Tokens;

public sealed class LinkToken : Entity
{
    public LinkToken(Guid id, Guid userId, string code, string role, DateTimeOffset expiresAt)
        : base(id)
    {
        UserId = userId;
        Code = code;
        Role = role;
        ExpiresAt = expiresAt;
    }

    public static LinkToken Restore(
        Guid id,
        Guid userId,
        string code,
        string role,
        DateTimeOffset expiresAt,
        TokenStatus status,
        Guid? acceptedBy,
        DateTimeOffset? acceptedAt)
    {
        var token = new LinkToken(id, userId, code, role, expiresAt)
        {
            Status = status,
            AcceptedBy = acceptedBy,
            AcceptedAt = acceptedAt
        };

        return token;
    }

    public Guid UserId { get; }
    public string Code { get; private set; }
    public string Role { get; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public TokenStatus Status { get; private set; } = TokenStatus.Pending;
    public Guid? AcceptedBy { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }

    public void MarkDeleted() => Status = TokenStatus.Deleted;

    public void Rotate(string code, DateTimeOffset expiresAt)
    {
        Code = code;
        ExpiresAt = expiresAt;
        Status = TokenStatus.Pending;
        AcceptedBy = null;
        AcceptedAt = null;
    }

    public void Accept(Guid userId, DateTimeOffset now)
    {
        Status = TokenStatus.Accepted;
        AcceptedBy = userId;
        AcceptedAt = now;
    }
}
