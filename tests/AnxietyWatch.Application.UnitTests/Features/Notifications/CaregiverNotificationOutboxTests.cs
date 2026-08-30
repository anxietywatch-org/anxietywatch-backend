using AnxietyWatch.Application.Abstractions.Notifications;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Domain.Caregivers;
using AnxietyWatch.Domain.Devices;
using AnxietyWatch.Domain.Notifications;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.Infrastructure.Notifications;
using AnxietyWatch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Notifications;

public sealed class CaregiverNotificationOutboxTests
{
    [Fact]
    public async Task SosAndSupportRequested_CreateOneJobPerCaregiverDeviceAndRemainDistinct()
    {
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        var links = Substitute.For<ILinkTokenRepository>();
        links.GetAsync(patientId, Arg.Any<CancellationToken>()).Returns([
            LinkToken.Restore(Guid.NewGuid(), patientId, "hidden", "family_member", DateTimeOffset.UtcNow.AddDays(-1), TokenStatus.Accepted, caregiverId, DateTimeOffset.UtcNow.AddDays(-2))]);
        var explicitLinks = Substitute.For<ICaregiverPatientLinkRepository>();
        explicitLinks.ListByPatientAsync(patientId, Arg.Any<CancellationToken>()).Returns([]);
        var devices = Substitute.For<IDeviceTokenRepository>();
        devices.GetForUserAsync(caregiverId, Arg.Any<CancellationToken>()).Returns([
            Device(caregiverId, "one"), Device(caregiverId, "two")]);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(patientId, Arg.Any<CancellationToken>()).Returns(new User(patientId, "Patient", "patient@example.test", "hash", "family"));
        var outbox = new InMemoryNotificationOutboxRepository();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var relationships = new CaregiverRelationshipResolver(links, explicitLinks);
        var sut = new CaregiverNotificationOutbox(relationships, devices, users, outbox, clock, NullLogger<CaregiverNotificationOutbox>.Instance);
        var eventId = Guid.NewGuid();

        await sut.EnsureNotificationJobsAsync(patientId, eventId, CaregiverNotificationType.Sos);
        await sut.EnsureNotificationJobsAsync(patientId, eventId, CaregiverNotificationType.SupportRequested);

        var jobs = await outbox.GetAllAsync();
        jobs.Should().HaveCount(4);
        jobs.Select(job => job.NotificationType).Distinct().Should().HaveCount(2);
        jobs.Select(job => job.DeviceRegistrationId).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task ExplicitOnlyRelationship_CreatesJobsForSosAndSupportRequested()
    {
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        var links = Substitute.For<ILinkTokenRepository>();
        links.GetAsync(patientId, Arg.Any<CancellationToken>()).Returns([]);
        var explicitLinks = Substitute.For<ICaregiverPatientLinkRepository>();
        explicitLinks.ListByPatientAsync(patientId, Arg.Any<CancellationToken>()).Returns([
            new CaregiverPatientLink(Guid.NewGuid(), caregiverId, patientId, DateTimeOffset.UtcNow, null)]);
        var devices = Substitute.For<IDeviceTokenRepository>();
        var device = Device(caregiverId, "explicit");
        devices.GetForUserAsync(caregiverId, Arg.Any<CancellationToken>()).Returns([device]);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(patientId, Arg.Any<CancellationToken>()).Returns(new User(patientId, "Patient", "patient@example.test", "hash", "family"));
        var outbox = new InMemoryNotificationOutboxRepository();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var sut = new CaregiverNotificationOutbox(new CaregiverRelationshipResolver(links, explicitLinks), devices, users, outbox, clock, NullLogger<CaregiverNotificationOutbox>.Instance);

        await sut.EnsureNotificationJobsAsync(patientId, Guid.NewGuid(), CaregiverNotificationType.Sos);
        await sut.EnsureNotificationJobsAsync(patientId, Guid.NewGuid(), CaregiverNotificationType.SupportRequested);

        var jobs = await outbox.GetAllAsync();
        jobs.Should().HaveCount(2);
        jobs.Should().OnlyContain(job => job.CaregiverId == caregiverId && job.DeviceRegistrationId == device.Id);
    }

    [Fact]
    public async Task HybridRelationship_CreatesOnlyOneJobForTheSameCaregiverDevice()
    {
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var links = Substitute.For<ILinkTokenRepository>();
        links.GetAsync(patientId, Arg.Any<CancellationToken>()).Returns([
            LinkToken.Restore(Guid.NewGuid(), patientId, "legacy", "family_member", DateTimeOffset.UtcNow.AddDays(1), TokenStatus.Accepted, caregiverId, DateTimeOffset.UtcNow)]);
        var explicitLinks = Substitute.For<ICaregiverPatientLinkRepository>();
        explicitLinks.ListByPatientAsync(patientId, Arg.Any<CancellationToken>()).Returns([
            new CaregiverPatientLink(Guid.NewGuid(), caregiverId, patientId, DateTimeOffset.UtcNow, null)]);
        var devices = Substitute.For<IDeviceTokenRepository>();
        var device = Device(caregiverId, "hybrid");
        devices.GetForUserAsync(caregiverId, Arg.Any<CancellationToken>()).Returns([device]);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(patientId, Arg.Any<CancellationToken>()).Returns(new User(patientId, "Patient", "patient@example.test", "hash", "family"));
        var outbox = new InMemoryNotificationOutboxRepository();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var sut = new CaregiverNotificationOutbox(new CaregiverRelationshipResolver(links, explicitLinks), devices, users, outbox, clock, NullLogger<CaregiverNotificationOutbox>.Instance);

        await sut.EnsureNotificationJobsAsync(patientId, eventId, CaregiverNotificationType.Sos);

        var jobs = await outbox.GetAllAsync();
        jobs.Should().ContainSingle();
        jobs[0].DedupeKey.Should().Be($"SOS:{eventId}:{caregiverId}:{device.Id}");
    }

    private static DeviceToken Device(Guid userId, string suffix) =>
        new(Guid.NewGuid(), userId, "android", $"registration-{suffix}", DateTimeOffset.UtcNow);
}
