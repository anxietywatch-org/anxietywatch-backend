namespace AnxietyWatch.Domain.Tokens;

public interface ILinkTokenRepository
{
    Task<IReadOnlyList<LinkToken>> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> TryAddAsync(LinkToken token, int maximum, CancellationToken cancellationToken = default);
    Task<LinkToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpdateAsync(LinkToken token, CancellationToken cancellationToken = default);
}
