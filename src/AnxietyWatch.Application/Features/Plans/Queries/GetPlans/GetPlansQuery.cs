using AnxietyWatch.Domain.Plans;
using MediatR;

namespace AnxietyWatch.Application.Features.Plans.Queries.GetPlans;

public sealed record GetPlansQuery : IRequest<IReadOnlyList<PlanDto>>;

public sealed class GetPlansQueryHandler(IPlanRepository plans)
    : IRequestHandler<GetPlansQuery, IReadOnlyList<PlanDto>>
{
    public async Task<IReadOnlyList<PlanDto>> Handle(
        GetPlansQuery request,
        CancellationToken cancellationToken)
    {
        var entities = await plans.GetAllAsync(cancellationToken);

        return entities
            .Select(plan => new PlanDto(
                plan.Type.ToString().ToLowerInvariant(),
                plan.Name,
                plan.PriceMonthly,
                plan.PriceYearly,
                plan.Features,
                plan.Limitations,
                plan.IdealFor))
            .ToArray();
    }
}
