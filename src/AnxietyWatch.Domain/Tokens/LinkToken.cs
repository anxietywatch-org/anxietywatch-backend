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

    public Guid UserId { get; }
    public string Code { get; }
    public string Role { get; }
    public DateTimeOffset ExpiresAt { get; }
    public TokenStatus Status { get; private set; } = TokenStatus.Pending;
    public Guid? AcceptedBy { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }

    public static LinkToken Rehydrate(
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

    public void MarkDeleted() => Status = TokenStatus.Deleted;

    public void Accept(Guid userId, DateTimeOffset now)
    {
        Status = TokenStatus.Accepted;
        AcceptedBy = userId;
        AcceptedAt = now;
    }
}
