using System.Text.Json;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Application.Features.Wearables;
using AnxietyWatch.Infrastructure.Persistence;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class InMemoryCaregiverPatientEventsTests
{
    [Fact]
    public async Task SuspectedDecisionIsOneItemAndSupportRequestedIsNotSos()
    {
        var patientId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var repository = new InMemoryWearableSyncRepository();
        await repository.TryStoreSuspectedEventAsync(patientId, Suspected(eventId, patientId, "DETECTED"));
        await repository.TryStoreEventDecisionAsync(patientId, Decision(eventId, patientId, "SUPPORT_REQUESTED"));

        var result = await repository.GetAsync(patientId, 50);

        result.Should().ContainSingle();
        result[0].Should().BeEquivalentTo(new PatientEventRecord(patientId, eventId, "SUSPECTED_EVENT", result[0].OccurredAt, "SUPPORT_REQUESTED"));
        result[0].Type.Should().NotBe("SOS");
    }

    [Fact]
    public async Task SosCancellationIsOneCancelledItem()
    {
        var patientId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var repository = new InMemoryWearableSyncRepository();
        await repository.TryStoreSosAsync(patientId, new SosTriggerRequest(eventId, Guid.NewGuid(), patientId, DateTimeOffset.UtcNow.AddMinutes(-1), "WATCH", null));
        await repository.TryStoreSosCancellationAsync(patientId, new SosCancelRequest(eventId, Guid.NewGuid(), patientId, DateTimeOffset.UtcNow, null));

        var result = await repository.GetAsync(patientId, 50);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { EventId = eventId, Type = "SOS", Status = "CANCELLED" });
    }

    [Fact]
    public async Task LimitAndOrderingAreGlobalAndDeterministic()
    {
        var patientId = Guid.NewGuid();
        var repository = new InMemoryWearableSyncRepository();
        for (var index = 0; index < 10; index++)
        {
            await repository.TryStoreSuspectedEventAsync(
                patientId,
                Suspected(Guid.NewGuid(), patientId, "DETECTED", DateTimeOffset.UtcNow.AddMinutes(-index)));
        }

        var result = await repository.GetAsync(patientId, 3);

        result.Should().HaveCount(3);
        result.Should().BeInDescendingOrder(item => item.OccurredAt);
        JsonSerializer.Serialize(result).Should().NotContain("Features");
    }

    [Fact]
    public async Task EqualTimestampsUseEventIdAsTieBreaker()
    {
        var patientId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var repository = new InMemoryWearableSyncRepository();
        await repository.TryStoreSuspectedEventAsync(patientId, Suspected(first, patientId, "DETECTED", timestamp));
        await repository.TryStoreSuspectedEventAsync(patientId, Suspected(second, patientId, "DETECTED", timestamp));

        var result = await repository.GetAsync(patientId, 50);

        result.Select(item => item.EventId).Should().ContainInOrder(second, first);
    }

    private static SuspectedEventRequest Suspected(Guid eventId, Guid patientId, string state, DateTimeOffset? detectedAt = null) =>
        new(eventId, Guid.NewGuid(), patientId, Guid.NewGuid(), 1, detectedAt ?? DateTimeOffset.UtcNow, state, 0.8, "v1",
            new(80, 100, 1, 10, 20, 30, 1, 2, 1, 0, 10), new(10, 80, 1, 0));

    private static EventDecisionRequest Decision(Guid eventId, Guid patientId, string response) =>
        new(eventId, Guid.NewGuid(), patientId, Guid.NewGuid(), 2, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, response);
}
