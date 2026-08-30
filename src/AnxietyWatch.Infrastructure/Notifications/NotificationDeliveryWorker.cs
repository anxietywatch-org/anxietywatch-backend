using AnxietyWatch.Application.Abstractions.Notifications;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Domain.Devices;
using AnxietyWatch.Domain.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.Infrastructure.Notifications;

public sealed class NotificationDeliveryWorker(
    INotificationOutboxRepository outbox,
    ICaregiverRelationshipResolver relationships,
    IDeviceTokenRepository devices,
    IPushNotificationSender sender,
    ISystemClock clock,
    ILogger<NotificationDeliveryWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 5;
    private readonly string workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Notification delivery worker started {WorkerId}", workerId);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var processed = await ProcessBatchAsync(20, stoppingToken);
                if (processed == 0) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        finally
        {
            logger.LogInformation("Notification delivery worker stopped {WorkerId}", workerId);
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
        if (!await relationships.IsLinkedAsync(job.CaregiverId, job.PatientId, ct))
        {
            logger.LogInformation("Notification job {JobId} skipped {Reason} attempt {Attempt}", job.Id, "RelationshipRevoked", job.AttemptCount);
            await outbox.MarkSkippedAsync(job.Id, "RelationshipRevoked", now, ct); return;
        }
        var device = await devices.GetByIdAsync(job.DeviceRegistrationId, ct);
        if (device is null || device.UserId != job.CaregiverId)
        {
            logger.LogInformation("Notification job {JobId} skipped {Reason} attempt {Attempt}", job.Id, "DeviceUnavailableOrTransferred", job.AttemptCount);
            await outbox.MarkSkippedAsync(job.Id, "DeviceUnavailableOrTransferred", now, ct); return;
        }

        var result = await sender.SendAsync(device.Token, job.Payload, ct);
        logger.LogInformation("Notification delivery {JobId} {Type} {EventId} {CaregiverId} {DeviceRegistrationId} attempt {Attempt} result {Result}",
            job.Id, job.NotificationType, job.EventId, job.CaregiverId, job.DeviceRegistrationId, job.AttemptCount, result.Outcome);
        switch (result.Outcome)
        {
            case PushSendOutcome.Success:
                logger.LogInformation("Notification job {JobId} sent attempt {Attempt}", job.Id, job.AttemptCount);
                await outbox.MarkSentAsync(job.Id, now, ct); break;
            case PushSendOutcome.PermanentInvalidRegistration:
                await devices.TryDeleteAsync(job.CaregiverId, device.Token, ct);
                logger.LogInformation("Notification job {JobId} skipped {Reason} attempt {Attempt}", job.Id, result.ErrorCode ?? "InvalidRegistration", job.AttemptCount);
                await outbox.MarkSkippedAsync(job.Id, result.ErrorCode ?? "InvalidRegistration", now, ct); break;
            case PushSendOutcome.TransientFailure when job.AttemptCount < MaxAttempts:
                logger.LogWarning("Notification job {JobId} retry {ErrorCode} attempt {Attempt}", job.Id, result.ErrorCode ?? "Transient", job.AttemptCount);
                await outbox.MarkRetryAsync(job.Id, result.ErrorCode ?? "Transient", now.Add(RetryDelay(job.AttemptCount)), now, ct); break;
            default:
                logger.LogError("Notification job {JobId} dead-lettered {ErrorCode} attempt {Attempt}", job.Id, result.ErrorCode ?? "PermanentFailure", job.AttemptCount);
                await outbox.MarkDeadLetterAsync(job.Id, result.ErrorCode ?? "PermanentFailure", now, ct); break;
        }
    }

    private static TimeSpan RetryDelay(int attempt) => attempt switch
    { 1 => TimeSpan.FromMinutes(1), 2 => TimeSpan.FromMinutes(5), 3 => TimeSpan.FromMinutes(15), _ => TimeSpan.FromHours(1) };
}
