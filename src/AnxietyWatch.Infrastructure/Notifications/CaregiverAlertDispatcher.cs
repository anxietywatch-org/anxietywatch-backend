using AnxietyWatch.Application.Abstractions.Notifications;
using AnxietyWatch.Domain.Devices;
using AnxietyWatch.Domain.Tokens;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.Infrastructure.Notifications;

public sealed class CaregiverAlertDispatcher(
    ILinkTokenRepository tokens,
    IDeviceTokenRepository devices,
    IPushNotifier notifier,
    ILogger<CaregiverAlertDispatcher> logger) : ICaregiverAlertDispatcher
{
    public Task DispatchSosAlertAsync(
        Guid patientUserId,
        Guid eventId,
        CancellationToken cancellationToken = default)
        => DispatchAsync(
            patientUserId,
            "Alerta SOS",
            $"Un paciente vinculado activó una alerta SOS (evento {eventId:N}).",
            "SOS",
            cancellationToken);

    public Task DispatchSupportRequestedAlertAsync(
        Guid patientUserId,
        Guid eventId,
        CancellationToken cancellationToken = default)
        => DispatchAsync(
            patientUserId,
            "El paciente pidió apoyo",
            $"Un paciente vinculado pidió apoyo durante un posible episodio (evento {eventId:N}).",
            "support-requested",
            cancellationToken);

    private async Task DispatchAsync(
        Guid patientUserId,
        string title,
        string body,
        string kind,
        CancellationToken cancellationToken)
    {
        try
        {
            var linked = await tokens.GetAsync(patientUserId, cancellationToken);
            var caregiverIds = linked
                .Where(token => token.Status == TokenStatus.Accepted && token.AcceptedBy.HasValue)
                .Select(token => token.AcceptedBy!.Value)
                .Distinct()
                .ToArray();
            var deviceTokens = new List<string>();
            foreach (var caregiverId in caregiverIds)
            {
                var caregiverDevices = await devices.GetForUserAsync(caregiverId, cancellationToken);
                deviceTokens.AddRange(caregiverDevices.Select(device => device.Token));
            }

            if (deviceTokens.Count > 0)
            {
                await notifier.NotifyAsync(deviceTokens, title, body, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Caregiver {Kind} alert dispatch failed for patient {PatientUserId}.",
                kind,
                patientUserId);
        }
    }
}
