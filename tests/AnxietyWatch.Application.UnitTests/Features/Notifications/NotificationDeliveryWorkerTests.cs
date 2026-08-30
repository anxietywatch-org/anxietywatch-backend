using AnxietyWatch.Application.Abstractions.Notifications;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Domain.Caregivers;
using AnxietyWatch.Domain.Devices;
using AnxietyWatch.Domain.Notifications;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Infrastructure.Notifications;
using AnxietyWatch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Notifications;

public sealed class NotificationDeliveryWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SuccessfulDelivery_MarksJobSent()
    {
        var (worker, outbox, sender, _, _) = CreateWorker();
        var job = AddJob(outbox);
        sender.SendAsync("registration-token", Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(new PushSendResult(PushSendOutcome.Success));

        await worker.ProcessBatchAsync(1);

        outbox.Get(job.Id).Status.Should().Be(NotificationDeliveryStatus.Sent);
        await sender.Received(1).SendAsync("registration-token", Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokedRelationship_SkipsWithoutSending()
    {
        var (worker, outbox, sender, relationships, _) = CreateWorker();
        var job = AddJob(outbox);
        relationships.IsLinkedAsync(job.CaregiverId, job.PatientId, Arg.Any<CancellationToken>()).Returns(false);

        await worker.ProcessBatchAsync(1);

        outbox.Get(job.Id).Status.Should().Be(NotificationDeliveryStatus.Skipped);
        outbox.Get(job.Id).LastErrorCode.Should().Be("RelationshipRevoked");
        await sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default);
    }

    [Fact]
    public async Task MissingOrTransferredDevice_SkipsWithoutSending()
    {
        var (worker, outbox, sender, _, devices) = CreateWorker();
        var job = AddJob(outbox);
        devices.GetByIdAsync(job.DeviceRegistrationId, Arg.Any<CancellationToken>()).Returns((DeviceToken?)null);

        await worker.ProcessBatchAsync(1);

        outbox.Get(job.Id).Status.Should().Be(NotificationDeliveryStatus.Skipped);
        outbox.Get(job.Id).LastErrorCode.Should().Be("DeviceUnavailableOrTransferred");
        await sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default);
    }

    [Fact]
    public async Task InvalidRegistration_RemovesDeviceAndSkipsJob()
    {
        var (worker, outbox, sender, _, devices) = CreateWorker();
        var job = AddJob(outbox);
        sender.SendAsync(Arg.Any<string>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(new PushSendResult(PushSendOutcome.PermanentInvalidRegistration, "Unregistered"));

        await worker.ProcessBatchAsync(1);

        outbox.Get(job.Id).Status.Should().Be(NotificationDeliveryStatus.Skipped);
        await devices.Received(1).TryDeleteAsync(job.CaregiverId, "registration-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransientFailure_RequeuesBeforeMaximumAttempts()
    {
        var (worker, outbox, sender, _, _) = CreateWorker();
        var job = AddJob(outbox);
        sender.SendAsync(Arg.Any<string>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(new PushSendResult(PushSendOutcome.TransientFailure, "Unavailable"));

        await worker.ProcessBatchAsync(1);

        outbox.Get(job.Id).Status.Should().Be(NotificationDeliveryStatus.Pending);
        outbox.Get(job.Id).NextAttemptAt.Should().BeAfter(Now);
    }

    [Fact]
    public async Task TransientFailureAtMaximumAttempts_DeadLetters()
    {
        var (worker, outbox, sender, _, _) = CreateWorker();
        var job = AddJob(outbox) with { AttemptCount = 4 };
        outbox.Replace(job);
        sender.SendAsync(Arg.Any<string>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(new PushSendResult(PushSendOutcome.TransientFailure, "Unavailable"));

        await worker.ProcessBatchAsync(1);

        outbox.Get(job.Id).Status.Should().Be(NotificationDeliveryStatus.DeadLetter);
    }

    [Fact]
    public async Task PermanentFailure_DeadLetters()
    {
        var (worker, outbox, sender, _, _) = CreateWorker();
        var job = AddJob(outbox);
        sender.SendAsync(Arg.Any<string>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(new PushSendResult(PushSendOutcome.PermanentOtherFailure, "FirebaseMessaging"));

        await worker.ProcessBatchAsync(1);

        outbox.Get(job.Id).Status.Should().Be(NotificationDeliveryStatus.DeadLetter);
    }

    [Fact]
    public async Task SentJob_IsNotSentTwice()
    {
        var (worker, outbox, sender, _, _) = CreateWorker();
        var job = AddJob(outbox);
        sender.SendAsync(Arg.Any<string>(), Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>())
            .Returns(new PushSendResult(PushSendOutcome.Success));

        await worker.ProcessBatchAsync(1);
        await worker.ProcessBatchAsync(1);

        await sender.Received(1).SendAsync("registration-token", Arg.Any<NotificationPayload>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExplicitLinkRemovedBeforeDelivery_SkipsPendingJob()
    {
        var outbox = new FakeOutbox();
        var patientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var caregiverId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var explicitLinks = new InMemoryCaregiverPatientLinkRepository();
        await explicitLinks.EnsureLinkAsync(caregiverId, patientId, null, Now);
        var tokens = new InMemoryLinkTokenRepository();
        var relationships = new CaregiverRelationshipResolver(tokens, explicitLinks);
        var devices = Substitute.For<IDeviceTokenRepository>();
        var sender = Substitute.For<IPushNotificationSender>();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);
        var device = new DeviceToken(Guid.NewGuid(), caregiverId, "android", "registration-token", Now);
        devices.GetByIdAsync(device.Id, Arg.Any<CancellationToken>()).Returns(device);
        var worker = new NotificationDeliveryWorker(outbox, relationships, devices, sender, clock, NullLogger<NotificationDeliveryWorker>.Instance);
        var job = AddJob(outbox) with { PatientId = patientId, CaregiverId = caregiverId, DeviceRegistrationId = device.Id };
        outbox.Replace(job);

        await explicitLinks.RemoveLinkAsync(caregiverId, patientId);
        await worker.ProcessBatchAsync(1);

        outbox.Get(job.Id).Status.Should().Be(NotificationDeliveryStatus.Skipped);
        outbox.Get(job.Id).LastErrorCode.Should().Be("RelationshipRevoked");
        await sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default);
    }

    private static (NotificationDeliveryWorker Worker, FakeOutbox Outbox, IPushNotificationSender Sender, ICaregiverRelationshipResolver Relationships, IDeviceTokenRepository Devices) CreateWorker()
    {
        var outbox = new FakeOutbox();
        var relationships = Substitute.For<ICaregiverRelationshipResolver>();
        var devices = Substitute.For<IDeviceTokenRepository>();
        var sender = Substitute.For<IPushNotificationSender>();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(Now);
        var device = new DeviceToken(Guid.NewGuid(), Guid.Parse("22222222-2222-2222-2222-222222222222"), "android", "registration-token", Now);
        devices.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(device);
        relationships.IsLinkedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        return (new NotificationDeliveryWorker(outbox, relationships, devices, sender, clock, NullLogger<NotificationDeliveryWorker>.Instance), outbox, sender, relationships, devices);
    }

    private static NotificationOutboxJob AddJob(FakeOutbox outbox)
    {
        var job = new NotificationOutboxJob(
            Guid.NewGuid(), "SOS:event:caregiver:device", CaregiverNotificationType.Sos,
            Guid.NewGuid(), Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.NewGuid(), new NotificationPayload("event", "Patient", "Alert"),
            NotificationDeliveryStatus.Pending, 0, Now, null, null, Now, null, null, null);
        outbox.Replace(job);
        return job;
    }

    private sealed class FakeOutbox : INotificationOutboxRepository
    {
        private readonly List<NotificationOutboxJob> jobs = [];
        public void Replace(NotificationOutboxJob job) { jobs.RemoveAll(existing => existing.Id == job.Id); jobs.Add(job); }
        public NotificationOutboxJob Get(Guid id) => jobs.Single(job => job.Id == id);
        public Task EnsureAsync(IReadOnlyCollection<NotificationOutboxJob> candidates, CancellationToken cancellationToken = default) { foreach (var job in candidates) Replace(job); return Task.CompletedTask; }
        public Task<NotificationOutboxJob?> ClaimNextAsync(DateTimeOffset now, DateTimeOffset leaseUntil, string claimedBy, CancellationToken cancellationToken = default)
        {
            var index = jobs.FindIndex(job => job.Status == NotificationDeliveryStatus.Pending && job.NextAttemptAt <= now);
            if (index < 0) return Task.FromResult<NotificationOutboxJob?>(null);
            var claimed = jobs[index] with { Status = NotificationDeliveryStatus.Processing, AttemptCount = jobs[index].AttemptCount + 1, LeaseUntil = leaseUntil, ClaimedBy = claimedBy, LastAttemptAt = now };
            jobs[index] = claimed;
            return Task.FromResult<NotificationOutboxJob?>(claimed);
        }
        public Task MarkSentAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default) { Update(id, job => job with { Status = NotificationDeliveryStatus.Sent, SentAt = at, LeaseUntil = null, ClaimedBy = null }); return Task.CompletedTask; }
        public Task MarkSkippedAsync(Guid id, string reason, DateTimeOffset at, CancellationToken cancellationToken = default) { Update(id, job => job with { Status = NotificationDeliveryStatus.Skipped, LastErrorCode = reason, LeaseUntil = null, ClaimedBy = null }); return Task.CompletedTask; }
        public Task MarkRetryAsync(Guid id, string errorCode, DateTimeOffset nextAttemptAt, DateTimeOffset at, CancellationToken cancellationToken = default) { Update(id, job => job with { Status = NotificationDeliveryStatus.Pending, LastErrorCode = errorCode, NextAttemptAt = nextAttemptAt, LeaseUntil = null, ClaimedBy = null }); return Task.CompletedTask; }
        public Task MarkDeadLetterAsync(Guid id, string errorCode, DateTimeOffset at, CancellationToken cancellationToken = default) { Update(id, job => job with { Status = NotificationDeliveryStatus.DeadLetter, LastErrorCode = errorCode, LeaseUntil = null, ClaimedBy = null }); return Task.CompletedTask; }
        public Task<IReadOnlyList<NotificationOutboxJob>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NotificationOutboxJob>>(jobs.ToArray());
        private void Update(Guid id, Func<NotificationOutboxJob, NotificationOutboxJob> update) { var index = jobs.FindIndex(job => job.Id == id); jobs[index] = update(jobs[index]); }
    }
}
