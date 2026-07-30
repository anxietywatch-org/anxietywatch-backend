namespace AnxietyWatch.Application.Common;

public sealed class ConflictException(string message) : Exception(message);

public sealed class UnauthorizedApplicationException(string message) : Exception(message);

public sealed class TooManyRequestsException(string message, int retryAfterSeconds) : Exception(message)
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}

public sealed class GoneException(string message) : Exception(message);
