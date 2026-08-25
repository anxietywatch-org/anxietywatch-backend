using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Application.Features.Wearables;
using AnxietyWatch.Infrastructure.Persistence;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class InMemoryLatestHeartRateTests
{
    [Fact]
    public async Task SelectsNewestValidSampleAcrossBatches()
    {
        var patientId = Guid.NewGuid();
        var repository = new InMemoryWearableSyncRepository();
        await repository.TryStoreTelemetryAsync(patientId, Batch(patientId, DateTimeOffset.UtcNow.AddMinutes(-2), 80));
        await repository.TryStoreTelemetryAsync(patientId, Batch(patientId, DateTimeOffset.UtcNow.AddMinutes(-1), null));
        await repository.TryStoreTelemetryAsync(patientId, Batch(patientId, DateTimeOffset.UtcNow, 82));

        var result = await repository.GetLatestAsync(patientId);

        result.Should().NotBeNull();
        result!.HeartRateBpm.Should().Be(82);
        result.Quality.Should().Be("good");
    }

    [Fact]
    public async Task DoesNotLeakAnotherPatientsTelemetryAndReturnsNullWhenAbsent()
    {
        var repository = new InMemoryWearableSyncRepository();
        var otherPatient = Guid.NewGuid();
        await repository.TryStoreTelemetryAsync(otherPatient, Batch(otherPatient, DateTimeOffset.UtcNow, 120));

        (await repository.GetLatestAsync(Guid.NewGuid())).Should().BeNull();
    }

    private static TelemetryBatchRequest Batch(Guid patientId, DateTimeOffset timestamp, double? heartRate) =>
        new(Guid.NewGuid(), Guid.NewGuid(), patientId, Guid.NewGuid(), timestamp, timestamp, 1,
            [new(timestamp, heartRate, [], null, null, null, new("good", "unknown", "onBody"))]);
}
