using System.Collections.Concurrent;
using AnxietyWatch.Domain.Episodes;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemoryEpisodeRepository : IEpisodeRepository
{
    private readonly ConcurrentDictionary<Guid, Episode> episodes = new();

    public Task<IReadOnlyList<Episode>> GetAsync(
        Guid userId,
        DateTimeOffset from,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Episode> result = episodes.Values
            .Where(episode => episode.UserId == userId && episode.Date >= from)
            .OrderByDescending(episode => episode.Date)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<int> CountAsync(Guid userId, DateTimeOffset from, CancellationToken cancellationToken = default) =>
        Task.FromResult(episodes.Values.Count(episode => episode.UserId == userId && episode.Date >= from));

    public Task AddAsync(Episode episode, CancellationToken cancellationToken = default)
    {
        episodes[episode.Id] = episode;
        return Task.CompletedTask;
    }
}
