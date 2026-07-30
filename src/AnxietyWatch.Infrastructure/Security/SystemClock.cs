using AnxietyWatch.Application.Abstractions.Time;

namespace AnxietyWatch.Infrastructure.Security;

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
