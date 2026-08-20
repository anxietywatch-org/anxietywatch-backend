using AnxietyWatch.Application.Features.Wearables;
using AnxietyWatch.Infrastructure.Persistence;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class InMemorySuspectedEventInferenceFlowTests : SuspectedEventInferenceFlowTests
{
    public InMemorySuspectedEventInferenceFlowTests()
    {
        SyncRepository = new InMemoryWearableSyncRepository();
        InferenceRepository = new InMemoryEventInferenceRepository();
        BuildService();
    }
}