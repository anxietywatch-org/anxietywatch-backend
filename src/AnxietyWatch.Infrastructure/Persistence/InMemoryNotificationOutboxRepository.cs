using AnxietyWatch.Domain.Notifications;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemoryNotificationOutboxRepository : INotificationOutboxRepository
{
    private readonly Dictionary<string, NotificationOutboxJob> jobs = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public Task EnsureAsync(IReadOnlyCollection<NotificationOutboxJob> candidates, CancellationToken cancellationToken = default)
    {
        lock (gate) foreach (var job in candidates) jobs.TryAdd(job.DedupeKey, job);
        return Task.CompletedTask;
    }

    public Task<NotificationOutboxJob?> ClaimNextAsync(DateTimeOffset now, DateTimeOffset leaseUntil, string claimedBy, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var job = jobs.Values.Where(j =>
                    (j.Status == NotificationDeliveryStatus.Pending && j.NextAttemptAt <= now) ||
                    (j.Status == NotificationDeliveryStatus.Processing && j.LeaseUntil <= now))
                .OrderBy(j => j.NextAttemptAt).ThenBy(j => j.CreatedAt).FirstOrDefault();
            if (job is null) return Task.FromResult<NotificationOutboxJob?>(null);
            var claimed = job with { Status = NotificationDeliveryStatus.Processing, AttemptCount = job.AttemptCount + 1, LeaseUntil = leaseUntil, ClaimedBy = claimedBy, LastAttemptAt = now };
            jobs[job.DedupeKey] = claimed;
            return Task.FromResult<NotificationOutboxJob?>(claimed);
        }
    }

    public Task MarkSentAsync(Guid id, DateTimeOffset sentAt, CancellationToken cancellationToken = default) => Update(id, j => j with { Status = NotificationDeliveryStatus.Sent, SentAt = sentAt, LeaseUntil = null, ClaimedBy = null });
    public Task MarkSkippedAsync(Guid id, string reason, DateTimeOffset at, CancellationToken cancellationToken = default) => Update(id, j => j with { Status = NotificationDeliveryStatus.Skipped, LastErrorCode = reason, LeaseUntil = null, ClaimedBy = null, LastAttemptAt = at });
    public Task MarkRetryAsync(Guid id, string errorCode, DateTimeOffset nextAttemptAt, DateTimeOffset at, CancellationToken cancellationToken = default) => Update(id, j => j with { Status = NotificationDeliveryStatus.Pending, LastErrorCode = errorCode, NextAttemptAt = nextAttemptAt, LeaseUntil = null, ClaimedBy = null, LastAttemptAt = at });
    public Task MarkDeadLetterAsync(Guid id, string errorCode, DateTimeOffset at, CancellationToken cancellationToken = default) => Update(id, j => j with { Status = NotificationDeliveryStatus.DeadLetter, LastErrorCode = errorCode, LeaseUntil = null, ClaimedBy = null, LastAttemptAt = at });
    public Task<IReadOnlyList<NotificationOutboxJob>> GetAllAsync(CancellationToken cancellationToken = default) { lock (gate) return Task.FromResult<IReadOnlyList<NotificationOutboxJob>>(jobs.Values.ToArray()); }

    private Task Update(Guid id, Func<NotificationOutboxJob, NotificationOutboxJob> update)
    {
        lock (gate)
        {
            var current = jobs.Values.Single(j => j.Id == id);
            jobs[current.DedupeKey] = update(current);
        }
        return Task.CompletedTask;
    }
}
