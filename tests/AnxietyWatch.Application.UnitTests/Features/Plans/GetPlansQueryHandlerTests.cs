using AnxietyWatch.Application.Features.Plans.Queries.GetPlans;
using AnxietyWatch.Domain.Plans;
using FluentAssertions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Plans;

public sealed class GetPlansQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnPlansAsApiDtos()
    {
        var repository = Substitute.For<IPlanRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Plan.Create(PlanType.Free, "Gratuito", 0, 0, ["Dashboard"], ["1 token"], "Uso personal")
        ]);

        var handler = new GetPlansQueryHandler(repository);
        var result = await handler.Handle(new GetPlansQuery(), CancellationToken.None);

        result.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Id = "free",
                Name = "Gratuito",
                PriceMonthly = 0m,
                PriceYearly = 0m,
                Features = new[] { "Dashboard" },
                Limitations = new[] { "1 token" },
                IdealFor = "Uso personal"
            });
    }
}
