namespace AnxietyWatch.Domain.Episodes;

public interface IEpisodeRepository
{
    Task<IReadOnlyList<Episode>> GetAsync(Guid userId, DateTimeOffset from, CancellationToken cancellationToken = default);
    Task<int> CountAsync(Guid userId, DateTimeOffset from, CancellationToken cancellationToken = default);
    Task AddAsync(Episode episode, CancellationToken cancellationToken = default);
}
