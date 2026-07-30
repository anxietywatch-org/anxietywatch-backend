using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Users;
using FluentValidation;
using MediatR;

namespace AnxietyWatch.Application.Features.Settings;

public sealed record UpdateProfileCommand(string FullName, string? AvatarUrl) : IRequest<ProfileResponse>;

public sealed record ProfileResponse(string FullName, string? AvatarUrl);

public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(command => command.FullName).NotEmpty().Length(2, 60);
        RuleFor(command => command.AvatarUrl).MaximumLength(2048);
    }
}

public sealed class UpdateProfileCommandHandler(ICurrentUser currentUser, IUserRepository users)
    : IRequestHandler<UpdateProfileCommand, ProfileResponse>
{
    public async Task<ProfileResponse> Handle(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        var user = await RequireUser(currentUser, users, cancellationToken);
        user.UpdateProfile(command.FullName.Trim(), command.AvatarUrl);
        await users.UpdateAsync(user, cancellationToken);
        return new ProfileResponse(user.FullName, user.AvatarUrl);
    }

    internal static async Task<User> RequireUser(
        ICurrentUser currentUser,
        IUserRepository users,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }

        return await users.GetByIdAsync(currentUser.UserId, cancellationToken)
            ?? throw new UnauthorizedApplicationException("The session is invalid.");
    }
}

public sealed record UpdateSettingsCommand(
    int AnxietyThreshold,
    bool PushNotifications,
    bool PrivateMode) : IRequest<SettingsResponse>;

public sealed record SettingsResponse(int AnxietyThreshold, bool PushNotifications, bool PrivateMode);

public sealed class UpdateSettingsCommandValidator : AbstractValidator<UpdateSettingsCommand>
{
    public UpdateSettingsCommandValidator() =>
        RuleFor(command => command.AnxietyThreshold).InclusiveBetween(0, 100);
}

public sealed class UpdateSettingsCommandHandler(ICurrentUser currentUser, IUserRepository users)
    : IRequestHandler<UpdateSettingsCommand, SettingsResponse>
{
    public async Task<SettingsResponse> Handle(UpdateSettingsCommand command, CancellationToken cancellationToken)
    {
        var user = await UpdateProfileCommandHandler.RequireUser(currentUser, users, cancellationToken);
        if (command.PrivateMode && currentUser.PlanId is not ("individual" or "family" or "professional"))
        {
            throw new ForbiddenException("Private mode requires an individual plan or higher.");
        }

        user.UpdateSettings(command.AnxietyThreshold, command.PushNotifications, command.PrivateMode);
        await users.UpdateAsync(user, cancellationToken);
        return new SettingsResponse(user.AnxietyThreshold, user.PushNotifications, user.PrivateMode);
    }
}
