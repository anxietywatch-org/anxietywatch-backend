using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Users;
using FluentValidation;
using MediatR;

namespace AnxietyWatch.Application.Features.Settings;

public sealed record UpdateProfileCommand(
    string FullName,
    string? AvatarUrl,
    string? Allergies = null,
    string? CurrentMedications = null,
    string? EmergencyContactName = null,
    string? EmergencyContactPhone = null,
    bool? PreviousAnxietyDiagnosis = null,
    string? TreatingProfessional = null) : IRequest<ProfileResponse>;

public sealed record ProfileResponse(
    string FullName,
    string? AvatarUrl,
    string? Allergies,
    string? CurrentMedications,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    bool? PreviousAnxietyDiagnosis,
    string? TreatingProfessional);

public sealed record GetProfileQuery : IRequest<ProfileResponse>;

public sealed class GetProfileQueryHandler(ICurrentUser currentUser, IUserRepository users)
    : IRequestHandler<GetProfileQuery, ProfileResponse>
{
    public async Task<ProfileResponse> Handle(GetProfileQuery request, CancellationToken cancellationToken)
    {
        var user = await UpdateProfileCommandHandler.RequireUser(currentUser, users, cancellationToken);
        return UpdateProfileCommandHandler.ToResponse(user);
    }
}

public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(command => command.FullName).NotEmpty().Length(2, 60);
        RuleFor(command => command.AvatarUrl).MaximumLength(2048);
        RuleFor(command => command.Allergies).MaximumLength(1000);
        RuleFor(command => command.CurrentMedications).MaximumLength(2000);
        RuleFor(command => command.EmergencyContactName).MaximumLength(120);
        RuleFor(command => command.EmergencyContactPhone).MaximumLength(40);
        RuleFor(command => command.TreatingProfessional).MaximumLength(200);
    }
}

public sealed class UpdateProfileCommandHandler(ICurrentUser currentUser, IUserRepository users)
    : IRequestHandler<UpdateProfileCommand, ProfileResponse>
{
    public async Task<ProfileResponse> Handle(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        var user = await RequireUser(currentUser, users, cancellationToken);
        user.UpdateProfile(command.FullName.Trim(), command.AvatarUrl);
        user.UpdateMedicalProfile(
            command.Allergies,
            command.CurrentMedications,
            command.EmergencyContactName,
            command.EmergencyContactPhone,
            command.PreviousAnxietyDiagnosis,
            command.TreatingProfessional);
        await users.UpdateAsync(user, cancellationToken);
        return ToResponse(user);
    }

    internal static ProfileResponse ToResponse(User user) => new(
        user.FullName,
        user.AvatarUrl,
        user.Allergies,
        user.CurrentMedications,
        user.EmergencyContactName,
        user.EmergencyContactPhone,
        user.PreviousAnxietyDiagnosis,
        user.TreatingProfessional);

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

public sealed record GetSettingsQuery : IRequest<SettingsResponse>;

public sealed class GetSettingsQueryHandler(ICurrentUser currentUser, IUserRepository users)
    : IRequestHandler<GetSettingsQuery, SettingsResponse>
{
    public async Task<SettingsResponse> Handle(GetSettingsQuery request, CancellationToken cancellationToken)
    {
        var user = await UpdateProfileCommandHandler.RequireUser(currentUser, users, cancellationToken);
        return new SettingsResponse(user.AnxietyThreshold, user.PushNotifications, user.PrivateMode);
    }
}

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
