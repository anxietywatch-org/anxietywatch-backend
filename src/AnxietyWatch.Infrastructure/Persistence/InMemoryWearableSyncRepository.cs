using System.Collections.Concurrent;
using AnxietyWatch.Application.Features.Wearables;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemoryWearableSyncRepository : IWearableSyncRepository
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, TelemetryBatchRequest>> telemetryBatches = new();
    private readonly ConcurrentDictionary<Guid, byte> sosIds = new();
    private readonly ConcurrentDictionary<Guid, byte> sosCancellationIds = new();
    private readonly ConcurrentDictionary<Guid, byte> suspectedEventIds = new();
    private readonly ConcurrentDictionary<Guid, byte> eventDecisionIds = new();

    public Task<bool> TryStoreTelemetryAsync(Guid userId, TelemetryBatchRequest batch, CancellationToken cancellationToken = default)
    {
        var userBatches = telemetryBatches.GetOrAdd(userId, static _ => new ConcurrentDictionary<Guid, TelemetryBatchRequest>());
        return Task.FromResult(userBatches.TryAdd(batch.BatchId, batch));
    }

    public Task<bool> TryStoreSosAsync(Guid userId, SosTriggerRequest trigger, CancellationToken cancellationToken = default) =>
        Task.FromResult(sosIds.TryAdd(trigger.EventId, 0));

    public Task<bool> TryStoreSosCancellationAsync(Guid userId, SosCancelRequest cancellation, CancellationToken cancellationToken = default) =>
        Task.FromResult(sosCancellationIds.TryAdd(cancellation.EventId, 0));

    public Task<bool> TryStoreSuspectedEventAsync(Guid userId, SuspectedEventRequest suspectedEvent, CancellationToken cancellationToken = default) =>
        Task.FromResult(suspectedEventIds.TryAdd(suspectedEvent.EventId, 0));

    public Task<bool> TryStoreEventDecisionAsync(Guid userId, EventDecisionRequest decision, CancellationToken cancellationToken = default) =>
        Task.FromResult(eventDecisionIds.TryAdd(decision.EventId, 0));

    public Task<TelemetryWindowResult> GetTelemetryWindowAsync(
        Guid userId,
        Guid deviceId,
        Guid sessionId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken = default)
    {
        var batches = new List<TelemetryBatchRequest>();
        if (telemetryBatches.TryGetValue(userId, out var userBatches))
        {
            batches.AddRange(userBatches.Values
                .Where(batch => batch.DeviceId == deviceId && batch.SessionId == sessionId));
        }

        return Task.FromResult(TelemetryWindowSelector.Select(batches, windowStart, windowEnd));
    }
}
