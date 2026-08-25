using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Caregivers;
using FluentAssertions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Caregivers;

public sealed class GetCaregiverLatestHeartRateQueryHandlerTests
{
    [Fact]
    public async Task Handle_AuthorizesBeforeReadingTelemetry()
    {
        var patientId = Guid.NewGuid();
        var authorizer = Substitute.For<ICaregiverAccessAuthorizer>();
        authorizer.RequireCaregiverAccessAsync(patientId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ForbiddenException("denied")));
        var heartRates = Substitute.For<IPatientHeartRateRepository>();
        var handler = new GetCaregiverLatestHeartRateQueryHandler(
            authorizer, heartRates, Substitute.For<ISystemClock>());

        var act = () => handler.Handle(new GetCaregiverLatestHeartRateQuery(patientId), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        await heartRates.DidNotReceive().GetLatestAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsSafeResponseAndClampsNegativeAge()
    {
        var patientId = Guid.NewGuid();
        var measuredAt = new DateTimeOffset(2026, 8, 25, 20, 30, 0, TimeSpan.Zero);
        var authorizer = Substitute.For<ICaregiverAccessAuthorizer>();
        var heartRates = Substitute.For<IPatientHeartRateRepository>();
        heartRates.GetLatestAsync(patientId, Arg.Any<CancellationToken>()).Returns(
            new LatestHeartRateRecord(82, measuredAt, "good"));
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(measuredAt.AddSeconds(-1));
        var handler = new GetCaregiverLatestHeartRateQueryHandler(authorizer, heartRates, clock);

        var result = await handler.Handle(new GetCaregiverLatestHeartRateQuery(patientId), CancellationToken.None);

        result.Should().BeEquivalentTo(new
        {
            HeartRateBpm = 82d,
            MeasuredAt = measuredAt,
            AgeSeconds = 0L,
            Quality = "good"
        });
    }
}
