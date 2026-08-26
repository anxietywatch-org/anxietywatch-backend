using System.Collections.Concurrent;
using AnxietyWatch.Domain.Devices;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemoryDeviceTokenRepository : IDeviceTokenRepository
{
    private readonly ConcurrentDictionary<string, DeviceToken> byToken = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public Task<IReadOnlyList<DeviceToken>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<DeviceToken> result = byToken.Values
                .Where(device => device.UserId == userId)
                .OrderBy(device => device.CreatedAt)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(byToken.TryGetValue(token, out var device) ? Clone(device) : null);
        }
    }

    public Task<DeviceToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var device = byToken.Values.FirstOrDefault(candidate => candidate.Id == id);
            return Task.FromResult(device is null ? null : Clone(device));
        }
    }

    public Task<DeviceToken> UpsertAsync(DeviceToken device, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var persisted = byToken.TryGetValue(device.Token, out var existing)
                ? DeviceToken.Restore(
                    existing.Id,
                    device.UserId,
                    device.Platform,
                    device.Token,
                    existing.CreatedAt,
                    device.UpdatedAt)
                : Clone(device);
            byToken[device.Token] = persisted;
            return Task.FromResult(Clone(persisted));
        }
    }

    public Task<bool> TryDeleteAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (byToken.TryGetValue(token, out var existing) && existing.UserId != userId)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(byToken.TryRemove(token, out _));
        }
    }

    private static DeviceToken Clone(DeviceToken device) => DeviceToken.Restore(
        device.Id,
        device.UserId,
        device.Platform,
        device.Token,
        device.CreatedAt,
        device.UpdatedAt);
}
