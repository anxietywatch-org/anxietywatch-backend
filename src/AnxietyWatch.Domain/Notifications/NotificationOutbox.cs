using AnxietyWatch.Domain.Common;

namespace AnxietyWatch.Domain.Notifications;

public enum CaregiverNotificationType { Sos, SupportRequested }
public enum NotificationDeliveryStatus { Pending, Processing, Sent, Skipped, DeadLetter }

public sealed record NotificationPayload(
    string EventId,
    string PatientName,
    string AlertMessage,
    string? Location = null,
    string? EmergencyPhone = null)
{
    public IReadOnlyDictionary<string, string> ToData()
    {
        var data = new Dictionary<string, string>
        {
            ["eventId"] = EventId,
            ["patientName"] = PatientName,
            ["alertMessage"] = AlertMessage
        };
        if (!string.IsNullOrWhiteSpace(Location)) data["location"] = Location;
        if (!string.IsNullOrWhiteSpace(EmergencyPhone)) data["emergencyPhone"] = EmergencyPhone;
        return data;
    }
}

public sealed record NotificationOutboxJob(
    Guid Id,
    string DedupeKey,
    CaregiverNotificationType NotificationType,
    Guid EventId,
    Guid PatientId,
    Guid CaregiverId,
    Guid DeviceRegistrationId,
    NotificationPayload Payload,
    NotificationDeliveryStatus Status,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? LeaseUntil,
    string? ClaimedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? LastAttemptAt,
    string? LastErrorCode);

public interface INotificationOutboxRepository
{
    Task EnsureAsync(IReadOnlyCollection<NotificationOutboxJob> jobs, CancellationToken cancellationToken = default);
    Task<NotificationOutboxJob?> ClaimNextAsync(DateTimeOffset now, DateTimeOffset leaseUntil, string claimedBy, CancellationToken cancellationToken = default);
    Task MarkSentAsync(Guid id, DateTimeOffset sentAt, CancellationToken cancellationToken = default);
    Task MarkSkippedAsync(Guid id, string reason, DateTimeOffset at, CancellationToken cancellationToken = default);
    Task MarkRetryAsync(Guid id, string errorCode, DateTimeOffset nextAttemptAt, DateTimeOffset at, CancellationToken cancellationToken = default);
    Task MarkDeadLetterAsync(Guid id, string errorCode, DateTimeOffset at, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationOutboxJob>> GetAllAsync(CancellationToken cancellationToken = default);
}
