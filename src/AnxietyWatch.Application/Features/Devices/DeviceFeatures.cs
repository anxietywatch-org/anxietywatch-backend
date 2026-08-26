using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Devices;
using FluentValidation;
using MediatR;

namespace AnxietyWatch.Application.Features.Devices;

public sealed record DeviceResponse(
    string Id,
    string Platform,
    DateTimeOffset RegisteredAt,
    DateTimeOffset UpdatedAt);

public sealed record RegisterDeviceCommand(string Platform, string Token) : IRequest<DeviceResponse>;

public sealed record UnregisterDeviceCommand(string Token) : IRequest<bool>;

public sealed record GetDevicesQuery : IRequest<IReadOnlyList<DeviceResponse>>;

public sealed class RegisterDeviceCommandValidator : AbstractValidator<RegisterDeviceCommand>
{
    private static readonly string[] Platforms = ["android", "ios", "web"];

    public RegisterDeviceCommandValidator()
    {
        RuleFor(command => command.Platform)
            .Must(value => Platforms.Contains(value, StringComparer.OrdinalIgnoreCase));
        RuleFor(command => command.Token).NotEmpty().MaximumLength(512);
    }
}

public sealed class UnregisterDeviceCommandValidator : AbstractValidator<UnregisterDeviceCommand>
{
    public UnregisterDeviceCommandValidator() => RuleFor(command => command.Token).NotEmpty().MaximumLength(512);
}

public sealed class RegisterDeviceCommandHandler(
    ICurrentUser currentUser,
    IDeviceTokenRepository devices,
    ISystemClock clock)
    : IRequestHandler<RegisterDeviceCommand, DeviceResponse>
{
    public async Task<DeviceResponse> Handle(RegisterDeviceCommand command, CancellationToken cancellationToken)
    {
        RequireAuthenticatedUser(currentUser);
        var now = clock.UtcNow;
        var device = new DeviceToken(
            Guid.NewGuid(),
            currentUser.UserId,
            command.Platform.ToLowerInvariant(),
            command.Token,
            now,
            now);
        var persisted = await devices.UpsertAsync(device, cancellationToken);
        return Map(persisted);
    }

    internal static void RequireAuthenticatedUser(ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }
    }

    internal static DeviceResponse Map(DeviceToken device) =>
        new(device.Id.ToString(), device.Platform, device.CreatedAt, device.UpdatedAt);
}

public sealed class UnregisterDeviceCommandHandler(ICurrentUser currentUser, IDeviceTokenRepository devices)
    : IRequestHandler<UnregisterDeviceCommand, bool>
{
    public async Task<bool> Handle(UnregisterDeviceCommand command, CancellationToken cancellationToken)
    {
        RegisterDeviceCommandHandler.RequireAuthenticatedUser(currentUser);
        return await devices.TryDeleteAsync(currentUser.UserId, command.Token, cancellationToken);
    }
}

public sealed class GetDevicesQueryHandler(ICurrentUser currentUser, IDeviceTokenRepository devices)
    : IRequestHandler<GetDevicesQuery, IReadOnlyList<DeviceResponse>>
{
    public async Task<IReadOnlyList<DeviceResponse>> Handle(
        GetDevicesQuery request,
        CancellationToken cancellationToken)
    {
        RegisterDeviceCommandHandler.RequireAuthenticatedUser(currentUser);
        var result = await devices.GetForUserAsync(currentUser.UserId, cancellationToken);
        return result.Select(RegisterDeviceCommandHandler.Map).ToArray();
    }
}
