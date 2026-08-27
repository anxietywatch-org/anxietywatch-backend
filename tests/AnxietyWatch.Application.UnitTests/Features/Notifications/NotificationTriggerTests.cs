using AnxietyWatch.Application.Abstractions.Notifications;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Features.Wearables;
using AnxietyWatch.Domain.Notifications;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Notifications;

public sealed class NotificationTriggerTests
{
    [Fact]
    public async Task SupportRequested_CreatesSupportNotificationJob()
    {
        var (handler, repository, outbox) = CreateHandler();
        var command = Command("SUPPORT_REQUESTED");
        repository.TryStoreEventDecisionAsync(Arg.Any<Guid>(), command.Decision, Arg.Any<CancellationToken>()).Returns(true);

        await handler.Handle(command, CancellationToken.None);

        await outbox.Received(1).EnsureNotificationJobsAsync(
            Arg.Any<Guid>(), command.Decision.EventId, CaregiverNotificationType.SupportRequested, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("USER_OK")]
    [InlineData("ACTIVITY_CONFIRMED")]
    public async Task NonSupportDecision_DoesNotCreateNotificationJob(string response)
    {
        var (handler, repository, outbox) = CreateHandler();
        var command = Command(response);
        repository.TryStoreEventDecisionAsync(Arg.Any<Guid>(), command.Decision, Arg.Any<CancellationToken>()).Returns(true);

        await handler.Handle(command, CancellationToken.None);

        await outbox.DidNotReceiveWithAnyArgs().EnsureNotificationJobsAsync(default, default, default, default);
    }

    private static (SubmitEventDecisionCommandHandler Handler, IWearableSyncRepository Repository, ICaregiverNotificationOutbox Outbox) CreateHandler()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var repository = Substitute.For<IWearableSyncRepository>();
        var outbox = Substitute.For<ICaregiverNotificationOutbox>();
        return (new SubmitEventDecisionCommandHandler(currentUser, repository, outbox), repository, outbox);
    }

    private static SubmitEventDecisionCommand Command(string response) => new(new EventDecisionRequest(
        Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), 1,
        DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, response));
}
