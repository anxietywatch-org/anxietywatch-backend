using AnxietyWatch.Domain.Common;

namespace AnxietyWatch.Domain.Plans;

public sealed class Plan : AggregateRoot
{
    private Plan() : base(Guid.Empty)
    {
    }

    private Plan(Guid id, PlanType type, string name, decimal priceMonthly, decimal priceYearly,
        IReadOnlyCollection<string> features, IReadOnlyCollection<string> limitations, string idealFor)
        : base(id)
    {
        Type = type;
        Name = name;
        PriceMonthly = priceMonthly;
        PriceYearly = priceYearly;
        Features = features;
        Limitations = limitations;
        IdealFor = idealFor;
    }

    public PlanType Type { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal PriceMonthly { get; private set; }
    public decimal PriceYearly { get; private set; }
    public IReadOnlyCollection<string> Features { get; private set; } = [];
    public IReadOnlyCollection<string> Limitations { get; private set; } = [];
    public string IdealFor { get; private set; } = string.Empty;

    public static Plan Create(PlanType type, string name, decimal priceMonthly, decimal priceYearly,
        IEnumerable<string> features, IEnumerable<string> limitations, string idealFor) =>
        new(Guid.NewGuid(), type, name, priceMonthly, priceYearly,
            features.ToArray(), limitations.ToArray(), idealFor);
}
