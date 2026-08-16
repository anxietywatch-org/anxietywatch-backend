using System.Collections.Concurrent;
using AnxietyWatch.Application.Features.Support;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemorySupportTicketRepository : ISupportTicketRepository
{
    private readonly ConcurrentDictionary<Guid, SupportTicket> tickets = new();

    public Task AddAsync(SupportTicket ticket, CancellationToken cancellationToken = default)
    {
        tickets[ticket.Id] = ticket;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SupportTicket>> GetByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SupportTicket> result = tickets.Values
            .Where(ticket => ticket.UserId == userId)
            .OrderByDescending(ticket => ticket.CreatedAt)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<SupportTicket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(tickets.GetValueOrDefault(id));
}
