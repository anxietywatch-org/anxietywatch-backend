using AnxietyWatch.Domain.Notifications;
using AnxietyWatch.Infrastructure.Persistence;
using FluentAssertions;

namespace AnxietyWatch.Application.UnitTests.Features.Notifications;

public sealed class InMemoryNotificationOutboxRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnsureAsync_IsIdempotentByDedupeKey()
    {
        var repository = new InMemoryNotificationOutboxRepository();
        var first = Job("same-key");
        var duplicate = first with { Id = Guid.NewGuid() };

        await repository.EnsureAsync([first, duplicate]);

        (await repository.GetAllAsync()).Should().ContainSingle(job => job.DedupeKey == "same-key");
    }

    [Fact]
    public async Task ExpiredProcessingLease_CanBeReclaimed()
    {
        var repository = new InMemoryNotificationOutboxRepository();
        var job = Job("lease");
        await repository.EnsureAsync([job]);
        var claimed = await repository.ClaimNextAsync(Now, Now.AddMinutes(2), "worker-1");
        claimed.Should().NotBeNull();

        var reclaimed = await repository.ClaimNextAsync(Now.AddMinutes(3), Now.AddMinutes(5), "worker-2");

        reclaimed.Should().NotBeNull();
        reclaimed!.ClaimedBy.Should().Be("worker-2");
    }

    [Theory]
    [InlineData("Sent")]
    [InlineData("Skipped")]
    [InlineData("DeadLetter")]
    public async Task TerminalJobs_AreNotReclaimed(string status)
    {
        var repository = new InMemoryNotificationOutboxRepository();
        var job = Job(status);
        await repository.EnsureAsync([job]);
        _ = await repository.ClaimNextAsync(Now, Now.AddMinutes(2), "worker-1");
        if (status == "Sent") await repository.MarkSentAsync(job.Id, Now);
        if (status == "Skipped") await repository.MarkSkippedAsync(job.Id, "revoked", Now);
        if (status == "DeadLetter") await repository.MarkDeadLetterAsync(job.Id, "permanent", Now);

        (await repository.ClaimNextAsync(Now.AddMinutes(10), Now.AddMinutes(12), "worker-2"))
            .Should().BeNull();
    }

    private static NotificationOutboxJob Job(string dedupeKey) => new(
        Guid.NewGuid(), dedupeKey, CaregiverNotificationType.Sos, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        new NotificationPayload("event", "Patient", "Alert"), NotificationDeliveryStatus.Pending, 0, Now, null, null, Now, null, null, null);
}
