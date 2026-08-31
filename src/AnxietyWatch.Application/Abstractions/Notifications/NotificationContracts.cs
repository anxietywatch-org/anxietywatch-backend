namespace AnxietyWatch.Application.Abstractions.Notifications;

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

    Task DispatchSupportRequestedAlertAsync(Guid patientUserId, Guid eventId, CancellationToken cancellationToken = default);
}