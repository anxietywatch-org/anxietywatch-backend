using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Caregivers;
using FluentAssertions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Caregivers;

public sealed class GetCaregiverPatientEventsQueryHandlerTests
{
    [Fact]
    public async Task Handle_AuthorizesBeforeReadingEvents()
    {
        var patientId = Guid.NewGuid();
        var authorizer = Substitute.For<ICaregiverAccessAuthorizer>();
        authorizer.RequireCaregiverAccessAsync(patientId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ForbiddenException("denied")));
        var events = Substitute.For<IPatientEventRepository>();
        var handler = new GetCaregiverPatientEventsQueryHandler(authorizer, events);

        var act = () => handler.Handle(new GetCaregiverPatientEventsQuery(patientId, 50), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        await events.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MapsOnlySafeTimelineFields()
    {
        var patientId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var authorizer = Substitute.For<ICaregiverAccessAuthorizer>();
        var events = Substitute.For<IPatientEventRepository>();
        events.GetAsync(patientId, 50, Arg.Any<CancellationToken>()).Returns(
            [new PatientEventRecord(patientId, eventId, "SUSPECTED_EVENT", DateTimeOffset.UtcNow, "USER_OK")]);
        var handler = new GetCaregiverPatientEventsQueryHandler(authorizer, events);

        var result = await handler.Handle(new GetCaregiverPatientEventsQuery(patientId, 50), CancellationToken.None);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { EventId = eventId, Type = "SUSPECTED_EVENT", Status = "USER_OK" });
    }
}
