using AnxietyWatch.Application.Abstractions.Notifications;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Domain.Devices;
using AnxietyWatch.Domain.Notifications;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;

namespace AnxietyWatch.Infrastructure.Notifications;

public sealed class CaregiverNotificationOutbox(
    ILinkTokenRepository links,
    IDeviceTokenRepository devices,
    IUserRepository users,
    INotificationOutboxRepository outbox,
    ISystemClock clock) : ICaregiverNotificationOutbox
{
    public async Task EnsureNotificationJobsAsync(
        Guid patientId,
        Guid eventId,
        CaregiverNotificationType type,
        CancellationToken cancellationToken = default)
    {
        var patient = await users.GetByIdAsync(patientId, cancellationToken);
        var patientName = string.IsNullOrWhiteSpace(patient?.FullName) ? "Paciente" : patient.FullName.Trim();
        var message = type == CaregiverNotificationType.Sos
            ? $"{patientName} activó una alerta SOS."
            : $"{patientName} solicitó apoyo.";
        var payload = new NotificationPayload(
            eventId.ToString(), patientName, message, EmergencyPhone: patient?.EmergencyContactPhone);

        var relationships = await links.GetAsync(patientId, cancellationToken);
        var caregiverIds = relationships
            .Where(link => link.Status == TokenStatus.Accepted &&
                           link.AcceptedBy.HasValue &&
                           string.Equals(link.Role, "family_member", StringComparison.Ordinal))
            .Select(link => link.AcceptedBy!.Value)
            .Distinct()
            .ToArray();
        var now = clock.UtcNow;
        var jobs = new List<NotificationOutboxJob>();
        foreach (var caregiverId in caregiverIds)
        {
            foreach (var device in (await devices.GetForUserAsync(caregiverId, cancellationToken)).DistinctBy(d => d.Id))
            {
                var typeName = type == CaregiverNotificationType.Sos ? "SOS" : "SUPPORT_REQUESTED";
                jobs.Add(new NotificationOutboxJob(
                    Guid.NewGuid(), $"{typeName}:{eventId}:{caregiverId}:{device.Id}", type,
                    eventId, patientId, caregiverId, device.Id, payload,
                    NotificationDeliveryStatus.Pending, 0, now, null, null, now, null, null, null));
            }
        }

        await outbox.EnsureAsync(jobs, cancellationToken);
    }
}
