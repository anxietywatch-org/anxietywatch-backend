using AnxietyWatch.Domain.Plans;
using FluentAssertions;

namespace AnxietyWatch.Domain.UnitTests.Plans;

public sealed class PlanTests
{
    [Fact]
    public void Create_ShouldKeepThePlanContract()
    {
        var plan = Plan.Create(
            PlanType.Free,
            "Gratuito",
            0,
            0,
            ["Dashboard"],
            ["1 token"],
            "Uso personal");

        plan.Type.Should().Be(PlanType.Free);
        plan.Name.Should().Be("Gratuito");
        plan.Features.Should().Contain("Dashboard");
        plan.Limitations.Should().Contain("1 token");
    }
}
