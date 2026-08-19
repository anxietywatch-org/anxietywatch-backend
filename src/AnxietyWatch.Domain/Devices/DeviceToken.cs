using AnxietyWatch.Domain.Common;

namespace AnxietyWatch.Domain.Devices;

public sealed class DeviceToken : Entity
{
    public DeviceToken(Guid id, Guid userId, string platform, string token, DateTimeOffset createdAt)
        : base(id)
    {
        UserId = userId;
        Platform = platform;
        Token = token;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static DeviceToken Restore(
        Guid id,
        Guid userId,
        string platform,
        string token,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var device = new DeviceToken(id, userId, platform, token, createdAt)
        {
            UpdatedAt = updatedAt
        };
        return device;
    }

    public Guid UserId { get; }
    public string Platform { get; }
    public string Token { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
}