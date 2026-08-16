using System.Collections.Concurrent;
using AnxietyWatch.Domain.Billing;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemoryBillingTransactionRepository : IBillingTransactionRepository
{
    private readonly ConcurrentDictionary<Guid, BillingTransaction> transactions = new();

    public Task AddAsync(BillingTransaction transaction, CancellationToken cancellationToken = default)
    {
        transactions[transaction.Id] = transaction;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BillingTransaction>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BillingTransaction>>(transactions.Values
            .Where(transaction => transaction.UserId == userId)
            .OrderByDescending(transaction => transaction.CreatedAt)
            .ToArray());
}
