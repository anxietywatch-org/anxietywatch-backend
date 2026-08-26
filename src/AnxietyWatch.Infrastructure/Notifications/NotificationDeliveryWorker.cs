using AnxietyWatch.Application.Abstractions.Notifications;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Domain.Devices;
using AnxietyWatch.Domain.Notifications;
using AnxietyWatch.Domain.Tokens;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.Infrastructure.Notifications;

public sealed class NotificationDeliveryWorker(
    INotificationOutboxRepository outbox,
    ILinkTokenRepository links,
    IDeviceTokenRepository devices,
    IPushNotificationSender sender,
    ISystemClock clock,
    ILogger<NotificationDeliveryWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 5;
    private readonly string workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = await ProcessBatchAsync(20, stoppingToken);
            if (processed == 0) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public async Task<int> ProcessBatchAsync(int maximum, CancellationToken cancellationToken = default)
    {
        var processed = 0;
        while (processed < maximum)
        {
            var now = clock.UtcNow;
            var job = await outbox.ClaimNextAsync(now, now.AddMinutes(2), workerId, cancellationToken);
            if (job is null) break;
            processed++;
            await ProcessAsync(job, now, cancellationToken);
        }
        return processed;
    }

    private async Task ProcessAsync(NotificationOutboxJob job, DateTimeOffset now, CancellationToken ct)
    {
        if (!await links.HasAcceptedCaregiverRelationshipAsync(job.PatientId, job.CaregiverId, ct))
        {
            await outbox.MarkSkippedAsync(job.Id, "RelationshipRevoked", now, ct); return;
        }
        var device = await devices.GetByIdAsync(job.DeviceRegistrationId, ct);
        if (device is null || device.UserId != job.CaregiverId)
        {
            await outbox.MarkSkippedAsync(job.Id, "DeviceUnavailableOrTransferred", now, ct); return;
        }

        var result = await sender.SendAsync(device.Token, job.Payload, ct);
        logger.LogInformation("Notification delivery {JobId} {Type} {EventId} {CaregiverId} {DeviceRegistrationId} attempt {Attempt} result {Result}",
            job.Id, job.NotificationType, job.EventId, job.CaregiverId, job.DeviceRegistrationId, job.AttemptCount, result.Outcome);
        switch (result.Outcome)
        {
            case PushSendOutcome.Success:
                await outbox.MarkSentAsync(job.Id, now, ct); break;
            case PushSendOutcome.PermanentInvalidRegistration:
                await devices.TryDeleteAsync(job.CaregiverId, device.Token, ct);
                await outbox.MarkSkippedAsync(job.Id, result.ErrorCode ?? "InvalidRegistration", now, ct); break;
            case PushSendOutcome.TransientFailure when job.AttemptCount < MaxAttempts:
                await outbox.MarkRetryAsync(job.Id, result.ErrorCode ?? "Transient", now.Add(RetryDelay(job.AttemptCount)), now, ct); break;
            default:
                await outbox.MarkDeadLetterAsync(job.Id, result.ErrorCode ?? "PermanentFailure", now, ct); break;
        }
    }

    private static TimeSpan RetryDelay(int attempt) => attempt switch
    { 1 => TimeSpan.FromMinutes(1), 2 => TimeSpan.FromMinutes(5), 3 => TimeSpan.FromMinutes(15), _ => TimeSpan.FromHours(1) };
}
