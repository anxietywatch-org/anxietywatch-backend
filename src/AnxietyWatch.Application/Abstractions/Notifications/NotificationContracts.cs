namespace AnxietyWatch.Application.Abstractions.Notifications;

using AnxietyWatch.Domain.Notifications;

public interface IPushNotifier
{
    Task NotifyAsync(
        IReadOnlyCollection<string> deviceTokens,
        string title,
        string body,
        CancellationToken cancellationToken = default);
}

public interface ICaregiverAlertDispatcher
{
    Task DispatchSosAlertAsync(Guid patientUserId, Guid eventId, CancellationToken cancellationToken = default);
}

public interface ICaregiverNotificationOutbox
{
    Task EnsureNotificationJobsAsync(
        Guid patientId,
        Guid eventId,
        CaregiverNotificationType type,
        CancellationToken cancellationToken = default);
}

public enum PushSendOutcome { Success, TransientFailure, PermanentInvalidRegistration, PermanentOtherFailure }
public sealed record PushSendResult(PushSendOutcome Outcome, string? ErrorCode = null);

public interface IPushNotificationSender
{
    Task<PushSendResult> SendAsync(
        string registrationToken,
        NotificationPayload payload,
        CancellationToken cancellationToken = default);
}
