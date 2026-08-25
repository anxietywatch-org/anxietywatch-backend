using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Domain.Episodes;
using AnxietyWatch.Domain.Users;
using FluentAssertions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Caregivers;

public sealed class GetCaregiverPatientEpisodesQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenPrivateModeIsOff_ReturnsEpisodeDetails()
    {
        var patientId = Guid.NewGuid();
        var authorizer = Substitute.For<ICaregiverAccessAuthorizer>();
        var users = Substitute.For<IUserRepository>();
        var episodes = Substitute.For<IEpisodeRepository>();
        var clock = Substitute.For<ISystemClock>();
        var now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(now);
        users.GetByIdAsync(patientId, Arg.Any<CancellationToken>()).Returns(
            new User(patientId, "Patient", "patient@example.test", "hash", "family"));
        episodes.GetAsync(patientId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new Episode(Guid.NewGuid(), patientId, now, 80, ["panic"], "note")]);
        var handler = new GetCaregiverPatientEpisodesQueryHandler(authorizer, users, episodes, clock);

        var result = await handler.Handle(new GetCaregiverPatientEpisodesQuery(patientId, 7), CancellationToken.None);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Intensity = 80, Symptoms = new[] { "panic" }, Notes = "note", DetailsHidden = false });
    }

    [Fact]
    public async Task Handle_WhenPrivateModeIsOn_RedactsSensitiveDetails()
    {
        var patientId = Guid.NewGuid();
        var authorizer = Substitute.For<ICaregiverAccessAuthorizer>();
        var users = Substitute.For<IUserRepository>();
        var episodes = Substitute.For<IEpisodeRepository>();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
        var patient = User.Restore(patientId, "Patient", "patient@example.test", "hash", "family", false, null,
            null, 70, true, true, 0, null, null, 0, 0);
        users.GetByIdAsync(patientId, Arg.Any<CancellationToken>()).Returns(patient);
        episodes.GetAsync(patientId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new Episode(Guid.NewGuid(), patientId, DateTimeOffset.UtcNow, 80, ["panic"], "note")]);
        var handler = new GetCaregiverPatientEpisodesQueryHandler(authorizer, users, episodes, clock);

        var result = await handler.Handle(new GetCaregiverPatientEpisodesQuery(patientId, 30), CancellationToken.None);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Intensity = 80, Symptoms = (IReadOnlyCollection<string>?)null, Notes = (string?)null, DetailsHidden = true });
    }

    [Fact]
    public async Task Handle_WhenPrivateModeStateIsUnresolved_FailsClosed()
    {
        var patientId = Guid.NewGuid();
        var authorizer = Substitute.For<ICaregiverAccessAuthorizer>();
        var users = Substitute.For<IUserRepository>();
        var episodes = Substitute.For<IEpisodeRepository>();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
        var patient = User.Restore(patientId, "Patient", "patient@example.test", "hash", "family", false, null,
            null, 70, true, false, 0, null, null, 0, 0, privateModeResolved: false);
        users.GetByIdAsync(patientId, Arg.Any<CancellationToken>()).Returns(patient);
        episodes.GetAsync(patientId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new Episode(Guid.NewGuid(), patientId, DateTimeOffset.UtcNow, 80, ["panic"], "note")]);
        var handler = new GetCaregiverPatientEpisodesQueryHandler(authorizer, users, episodes, clock);

        var result = await handler.Handle(new GetCaregiverPatientEpisodesQuery(patientId, 7), CancellationToken.None);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Symptoms = (IReadOnlyCollection<string>?)null, Notes = (string?)null, DetailsHidden = true });
    }

    [Fact]
    public async Task Handle_AuthorizesBeforeLoadingPatientOrEpisodes()
    {
        var patientId = Guid.NewGuid();
        var authorizer = Substitute.For<ICaregiverAccessAuthorizer>();
        authorizer.RequireCaregiverAccessAsync(patientId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ForbiddenException("denied")));
        var users = Substitute.For<IUserRepository>();
        var episodes = Substitute.For<IEpisodeRepository>();
        var handler = new GetCaregiverPatientEpisodesQueryHandler(
            authorizer, users, episodes, Substitute.For<ISystemClock>());

        var act = () => handler.Handle(new GetCaregiverPatientEpisodesQuery(patientId, 7), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        await users.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await episodes.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
