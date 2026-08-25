using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Domain.Users;
using FluentAssertions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Caregivers;

public sealed class GetPatientDetailQueryHandlerTests
{
    [Fact]
    public async Task Handle_RequiresRelationshipBeforeLoadingPatient()
    {
        var patientId = Guid.NewGuid();
        var authorizer = Substitute.For<ICaregiverAccessAuthorizer>();
        var users = Substitute.For<IUserRepository>();
        var handler = new GetPatientDetailQueryHandler(
            authorizer, users);
        var patient = new User(patientId, "Patient", "patient@example.test", "hash", "free");
        users.GetByIdAsync(patientId, Arg.Any<CancellationToken>()).Returns(patient);

        var response = await handler.Handle(new GetPatientDetailQuery(patientId), CancellationToken.None);

        response.Should().Be(new PatientDetailResponse(patientId, "Patient", null));
        await authorizer.Received(1).RequireCaregiverAccessAsync(patientId, Arg.Any<CancellationToken>());
        await users.Received(1).GetByIdAsync(patientId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNotLoadPatientWhenAuthorizationFails()
    {
        var patientId = Guid.NewGuid();
        var authorizer = Substitute.For<ICaregiverAccessAuthorizer>();
        authorizer.RequireCaregiverAccessAsync(patientId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ForbiddenException("denied")));
        var users = Substitute.For<IUserRepository>();
        var handler = new GetPatientDetailQueryHandler(
            authorizer, users);

        var act = () => handler.Handle(new GetPatientDetailQuery(patientId), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        await users.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAuthorizedPatientIsMissing_ThrowsNotFound()
    {
        var patientId = Guid.NewGuid();
        var authorizer = Substitute.For<ICaregiverAccessAuthorizer>();
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(patientId, Arg.Any<CancellationToken>()).Returns((User?)null);
        var handler = new GetPatientDetailQueryHandler(
            authorizer, users);

        var act = () => handler.Handle(new GetPatientDetailQuery(patientId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
