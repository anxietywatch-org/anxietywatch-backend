namespace AnxietyWatch.Domain.Devices;

public interface IDeviceTokenRepository
{
    Task<IReadOnlyList<DeviceToken>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<DeviceToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DeviceToken> UpsertAsync(DeviceToken device, CancellationToken cancellationToken = default);
    Task<bool> TryDeleteAsync(Guid userId, string token, CancellationToken cancellationToken = default);
}
