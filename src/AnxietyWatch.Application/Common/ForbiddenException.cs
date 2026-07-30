namespace AnxietyWatch.Application.Common;

public sealed class ForbiddenException(string message) : Exception(message);

public sealed class NotFoundException(string message) : Exception(message);
