namespace AnxietyWatch.Application.Common;

public sealed class ConflictException(string message) : Exception(message);

public sealed class UnauthorizedApplicationException(string message) : Exception(message);

public sealed class TooManyRequestsException(string message, int retryAfterSeconds) : Exception(message)
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}

public sealed class GoneException(string message) : Exception(message);

public sealed class EmailDeliveryException(
    string message,
    bool deliveryMayHaveSucceeded = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    public bool DeliveryMayHaveSucceeded { get; } = deliveryMayHaveSucceeded;
}

public sealed class ServiceUnavailableException(
    string message,
    int retryAfterSeconds = 30,
    Exception? innerException = null) : Exception(message, innerException)
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}
