using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Application.Features.Events;
using FluentAssertions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Events;

public sealed class GetPatientEventHistoryQueryHandlerTests
{
    [Fact]
    public async Task Handle_RequiresAuthenticatedCurrentUser()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);
        var events = Substitute.For<IPatientEventRepository>();
        var handler = new GetPatientEventHistoryQueryHandler(currentUser, events);

        var act = () => handler.Handle(new GetPatientEventHistoryQuery(50), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedApplicationException>();
        await events.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UsesJwtUserIdAndMapsOnlySafeFields()
    {
        var patientId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(patientId);
        var events = Substitute.For<IPatientEventRepository>();
        events.GetAsync(patientId, 50, Arg.Any<CancellationToken>()).Returns(
            [new PatientEventRecord(patientId, eventId, "SUSPECTED_EVENT", DateTimeOffset.UtcNow, "SUPPORT_REQUESTED")]);
        var handler = new GetPatientEventHistoryQueryHandler(currentUser, events);

        var result = await handler.Handle(new GetPatientEventHistoryQuery(50), CancellationToken.None);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { EventId = eventId, Type = "SUSPECTED_EVENT", Status = "SUPPORT_REQUESTED" });
        await events.Received(1).GetAsync(patientId, 50, Arg.Any<CancellationToken>());
    }
}
