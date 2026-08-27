using AnxietyWatch.Application.Abstractions.Notifications;
using AnxietyWatch.Application.Abstractions.Time;
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
        var devices = Substitute.For<IDeviceTokenRepository>();
        devices.GetForUserAsync(caregiverId, Arg.Any<CancellationToken>()).Returns([
            Device(caregiverId, "one"), Device(caregiverId, "two")]);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(patientId, Arg.Any<CancellationToken>()).Returns(new User(patientId, "Patient", "patient@example.test", "hash", "family"));
        var outbox = new InMemoryNotificationOutboxRepository();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var sut = new CaregiverNotificationOutbox(links, devices, users, outbox, clock, NullLogger<CaregiverNotificationOutbox>.Instance);
        var eventId = Guid.NewGuid();

        await sut.EnsureNotificationJobsAsync(patientId, eventId, CaregiverNotificationType.Sos);
        await sut.EnsureNotificationJobsAsync(patientId, eventId, CaregiverNotificationType.SupportRequested);

        var jobs = await outbox.GetAllAsync();
        jobs.Should().HaveCount(4);
        jobs.Select(job => job.NotificationType).Distinct().Should().HaveCount(2);
        jobs.Select(job => job.DeviceRegistrationId).Distinct().Should().HaveCount(2);
    }

    private static DeviceToken Device(Guid userId, string suffix) =>
        new(Guid.NewGuid(), userId, "android", $"registration-{suffix}", DateTimeOffset.UtcNow);
}
