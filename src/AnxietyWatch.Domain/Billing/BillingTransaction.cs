namespace AnxietyWatch.Domain.Billing;

public sealed record BillingTransaction(
    Guid Id,
    Guid UserId,
    string PlanId,
    string BillingCycle,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    string Status = "succeeded",
    bool Simulated = true);

public interface IBillingTransactionRepository
{
    Task AddAsync(BillingTransaction transaction, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BillingTransaction>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
