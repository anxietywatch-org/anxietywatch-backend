using AnxietyWatch.Domain.Devices;
using AnxietyWatch.Infrastructure.Persistence;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class InMemoryDeviceRegistrationTests
{
    private readonly InMemoryDeviceTokenRepository devices = new();

    [Fact]
    public async Task UpsertTransfersOwnershipPreservesIdentityAndAllowsMultipleTokens()
    {
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var token = "token-1";

        var first = await devices.UpsertAsync(Device(firstUser, token, "android", createdAt));
        var transferred = await devices.UpsertAsync(Device(secondUser, token, "ios", DateTimeOffset.UtcNow));
        await devices.UpsertAsync(Device(secondUser, "token-2", "android", DateTimeOffset.UtcNow));

        transferred.Id.Should().Be(first.Id);
        transferred.CreatedAt.Should().Be(first.CreatedAt);
        transferred.UserId.Should().Be(secondUser);
        transferred.Platform.Should().Be("ios");
        (await devices.GetForUserAsync(firstUser)).Should().BeEmpty();
        (await devices.GetForUserAsync(secondUser)).Should().HaveCount(2);
    }

    [Fact]
    public async Task ConcurrentOwnershipRaceLeavesExactlyOneFinalOwner()
    {
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        const string token = "shared-token";

        await Task.WhenAll(
            devices.UpsertAsync(Device(firstUser, token, "android", DateTimeOffset.UtcNow)),
            devices.UpsertAsync(Device(secondUser, token, "ios", DateTimeOffset.UtcNow)));

        var persisted = await devices.GetByTokenAsync(token);
        persisted.Should().NotBeNull();
        new[] { firstUser, secondUser }.Should().Contain(persisted!.UserId);
        var total = (await devices.GetForUserAsync(firstUser)).Count +
                    (await devices.GetForUserAsync(secondUser)).Count;
        total.Should().Be(1);
    }

    private static DeviceToken Device(Guid userId, string token, string platform, DateTimeOffset now) =>
        new(Guid.NewGuid(), userId, platform, token, now, now);
}
