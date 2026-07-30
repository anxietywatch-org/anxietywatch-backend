using System.Net;
using System.Text.Json;
using AnxietyWatch.Application.Common;
using FluentValidation;

namespace AnxietyWatch.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled request failure. TraceId: {TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = exception switch
            {
                ValidationException => StatusCodes.Status400BadRequest,
                ConflictException => StatusCodes.Status409Conflict,
                ForbiddenException => StatusCodes.Status403Forbidden,
                NotFoundException => StatusCodes.Status404NotFound,
                UnauthorizedApplicationException => StatusCodes.Status401Unauthorized,
                TooManyRequestsException => StatusCodes.Status429TooManyRequests,
                GoneException => StatusCodes.Status410Gone,
                _ => (int)HttpStatusCode.InternalServerError
            };
            if (exception is TooManyRequestsException tooManyRequests)
            {
                context.Response.Headers.RetryAfter = tooManyRequests.RetryAfterSeconds.ToString();
            }

            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                type = $"https://httpstatuses.com/{context.Response.StatusCode}",
                title = exception.Message,
                status = context.Response.StatusCode,
                traceId = context.TraceIdentifier
            }));
        }
    }
}
