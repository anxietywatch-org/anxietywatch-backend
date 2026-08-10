using System.Collections.Concurrent;
using AnxietyWatch.Application.Features.Wearables;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemoryWearableSyncRepository : IWearableSyncRepository
{
    private readonly ConcurrentDictionary<Guid, byte> telemetryIds = new();
    private readonly ConcurrentDictionary<Guid, byte> sosIds = new();

    public Task<bool> TryStoreTelemetryAsync(Guid userId, TelemetryBatchRequest batch, CancellationToken cancellationToken = default) =>
        Task.FromResult(telemetryIds.TryAdd(batch.BatchId, 0));

    public Task<bool> TryStoreSosAsync(Guid userId, SosTriggerRequest trigger, CancellationToken cancellationToken = default) =>
        Task.FromResult(sosIds.TryAdd(trigger.EventId, 0));
}
