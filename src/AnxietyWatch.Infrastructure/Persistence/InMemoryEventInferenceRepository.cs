using System.Collections.Concurrent;
using AnxietyWatch.Application.Features.Wearables;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed record StoredEventInference(Guid UserId, EventInferenceResult Result);

public sealed class InMemoryEventInferenceRepository : IEventInferenceRepository
{
    private readonly ConcurrentDictionary<Guid, StoredEventInference> inferences = new();

    public Task<bool> TryStoreInferenceAsync(
        Guid userId,
        EventInferenceResult result,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(inferences.TryAdd(result.EventId, new StoredEventInference(userId, result)));

    public Task<EventInferenceResult?> GetInferenceAsync(
        Guid userId,
        Guid eventId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            inferences.TryGetValue(eventId, out var stored) && stored.UserId == userId
                ? stored.Result
                : null);
}