using System.Collections.Concurrent;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Application.Features.Wearables;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed record StoredTelemetryBatch(Guid UserId, TelemetryBatchRequest Batch);

public sealed class InMemoryWearableSyncRepository : IWearableSyncRepository, IPatientEventRepository
{
    private readonly ConcurrentDictionary<Guid, StoredTelemetryBatch> telemetryBatches = new();
    private readonly ConcurrentDictionary<string, PatientEventRecord> events = new();

    public Task<bool> TryStoreTelemetryAsync(Guid userId, TelemetryBatchRequest batch, CancellationToken cancellationToken = default) =>
        Task.FromResult(telemetryBatches.TryAdd(batch.BatchId, new StoredTelemetryBatch(userId, batch)));

    public Task<bool> TryStoreSosAsync(Guid userId, SosTriggerRequest trigger, CancellationToken cancellationToken = default) =>
        Task.FromResult(events.TryAdd($"sos:{trigger.EventId}", new PatientEventRecord(userId, trigger.EventId, "SOS", trigger.TriggeredAt, "TRIGGERED")));

    public Task<bool> TryStoreSosCancellationAsync(Guid userId, SosCancelRequest cancellation, CancellationToken cancellationToken = default) =>
        Task.FromResult(events.TryAdd($"cancel:{cancellation.EventId}", new PatientEventRecord(userId, cancellation.EventId, "SOS_CANCELLATION", cancellation.CancelledAt, "CANCELLED")));

    public Task<bool> TryStoreSuspectedEventAsync(Guid userId, SuspectedEventRequest suspectedEvent, CancellationToken cancellationToken = default) =>
        Task.FromResult(events.TryAdd($"suspected:{suspectedEvent.EventId}", new PatientEventRecord(userId, suspectedEvent.EventId, "SUSPECTED_EVENT", suspectedEvent.DetectedAt, suspectedEvent.State)));

    public Task<bool> TryStoreEventDecisionAsync(Guid userId, EventDecisionRequest decision, CancellationToken cancellationToken = default) =>
        Task.FromResult(events.TryAdd($"decision:{decision.EventId}", new PatientEventRecord(userId, decision.EventId, "EVENT_DECISION", decision.RespondedAt, decision.Response)));

    public Task<IReadOnlyList<PatientEventRecord>> GetAsync(Guid patientId, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PatientEventRecord>>(events.Values
            .Where(record => record.PatientId == patientId)
            .GroupBy(record => (record.Type is "SOS" or "SOS_CANCELLATION" ? "sos:" : "event:") + record.EventId)
            .Select(group => Merge(group))
            .OrderByDescending(record => record.OccurredAt)
            .ThenByDescending(record => record.EventId)
            .Take(limit)
            .ToArray());

    private static PatientEventRecord Merge(IEnumerable<PatientEventRecord> records)
    {
        var ordered = records.OrderByDescending(record => record.OccurredAt).ToArray();
        var first = ordered.FirstOrDefault(record => record.Type == "SUSPECTED_EVENT") ?? ordered[0];
        var decision = ordered.FirstOrDefault(record => record.Type == "EVENT_DECISION");
        var cancellation = ordered.FirstOrDefault(record => record.Type == "SOS_CANCELLATION");
        return cancellation is not null
            ? first with { Type = "SOS", Status = "CANCELLED", OccurredAt = cancellation.OccurredAt }
            : decision is not null && first.Type == "SUSPECTED_EVENT"
                ? first with { Status = decision.Status }
                : first;
    }

    public Task<TelemetryWindowResult> GetTelemetryWindowAsync(
        Guid userId,
        Guid deviceId,
        Guid sessionId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken = default)
    {
        var batches = telemetryBatches.Values
            .Where(stored => stored.UserId == userId &&
                             stored.Batch.DeviceId == deviceId &&
                             stored.Batch.SessionId == sessionId)
            .Select(stored => stored.Batch)
            .ToList();

        return Task.FromResult(TelemetryWindowSelector.Select(batches, windowStart, windowEnd));
    }
}
