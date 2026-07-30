namespace AnxietyWatch.Application.Features.Plans.Queries.GetPlans;

public sealed record PlanDto(
    string Id,
    string Name,
    decimal PriceMonthly,
    decimal PriceYearly,
    IReadOnlyCollection<string> Features,
    IReadOnlyCollection<string> Limitations,
    string IdealFor);
