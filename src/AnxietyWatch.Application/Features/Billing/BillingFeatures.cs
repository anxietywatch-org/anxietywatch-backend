using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Billing;
using AnxietyWatch.Domain.Plans;
using AnxietyWatch.Domain.Users;
using FluentValidation;
using MediatR;

namespace AnxietyWatch.Application.Features.Billing;

public sealed record SimulatePaymentCommand(string PlanId, string BillingCycle) : IRequest<SimulatedPaymentResponse>;

public sealed record SimulatedPaymentResponse(
    string TransactionId,
    string PlanId,
    string BillingCycle,
    decimal Amount,
    string Currency,
    string Status,
    bool Simulated,
    DateTimeOffset CreatedAt);

public sealed class SimulatePaymentCommandValidator : AbstractValidator<SimulatePaymentCommand>
{
    public SimulatePaymentCommandValidator()
    {
        RuleFor(command => command.PlanId).NotEmpty().MaximumLength(40);
        RuleFor(command => command.BillingCycle)
            .Must(value => value is not null && new[] { "monthly", "yearly" }
                .Contains(value, StringComparer.OrdinalIgnoreCase));
    }
}

public sealed class SimulatePaymentCommandHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    IPlanRepository plans,
    IBillingTransactionRepository transactions,
    ISystemClock clock)
    : IRequestHandler<SimulatePaymentCommand, SimulatedPaymentResponse>
{
    public async Task<SimulatedPaymentResponse> Handle(SimulatePaymentCommand command, CancellationToken cancellationToken)
    {
        RequireAuthenticatedUser();
        var plan = (await plans.GetAllAsync(cancellationToken)).FirstOrDefault(candidate =>
            string.Equals(candidate.Type.ToString(), command.PlanId, StringComparison.OrdinalIgnoreCase));
        if (plan is null) throw new NotFoundException("The selected plan was not found.");
        if (plan.Type == PlanType.Free) throw new ConflictException("The free plan does not require payment.");

        var cycle = command.BillingCycle.ToLowerInvariant();
        var amount = cycle == "yearly" ? plan.PriceYearly : plan.PriceMonthly;
        var now = clock.UtcNow;
        if (!await users.UpdatePlanAsync(currentUser.UserId, plan.Type.ToString().ToLowerInvariant(), cancellationToken))
            throw new UnauthorizedApplicationException("The session is invalid.");

        var transaction = new BillingTransaction(
            Guid.NewGuid(), currentUser.UserId, plan.Type.ToString().ToLowerInvariant(), cycle,
            amount, "MXN", now);
        await transactions.AddAsync(transaction, cancellationToken);
        return new SimulatedPaymentResponse(transaction.Id.ToString(), transaction.PlanId, transaction.BillingCycle,
            transaction.Amount, transaction.Currency, transaction.Status, true, transaction.CreatedAt);
    }

    private void RequireAuthenticatedUser()
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
            throw new UnauthorizedApplicationException("Authentication is required.");
    }
}

public sealed record BillingSummaryResponse(
    string PlanId,
    string BillingCycle,
    string Status,
    SimulatedPaymentResponse? LastPayment,
    IReadOnlyList<SimulatedPaymentResponse> Transactions,
    bool Simulated);

public sealed record GetBillingSummaryQuery : IRequest<BillingSummaryResponse>;

public sealed class GetBillingSummaryQueryHandler(
    ICurrentUser currentUser,
    IBillingTransactionRepository transactions,
    IUserRepository users)
    : IRequestHandler<GetBillingSummaryQuery, BillingSummaryResponse>
{
    public async Task<BillingSummaryResponse> Handle(GetBillingSummaryQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
            throw new UnauthorizedApplicationException("Authentication is required.");
        var planId = await CurrentPlanAuthority.RequirePlanIdAsync(currentUser, users, cancellationToken);
        var items = (await transactions.GetByUserAsync(currentUser.UserId, cancellationToken))
            .Select(Map).ToArray();
        return new BillingSummaryResponse(
            planId,
            items.FirstOrDefault()?.BillingCycle ?? "monthly",
            "active",
            items.FirstOrDefault(),
            items,
            true);
    }

    private static SimulatedPaymentResponse Map(BillingTransaction transaction) =>
        new(transaction.Id.ToString(), transaction.PlanId, transaction.BillingCycle, transaction.Amount,
            transaction.Currency, transaction.Status, transaction.Simulated, transaction.CreatedAt);
}
