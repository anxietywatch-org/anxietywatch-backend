using System.Collections.Concurrent;
using AnxietyWatch.Application.Abstractions.MlInference;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class FakeMlInferenceClient : IMlInferenceClient
{
    private readonly ConcurrentQueue<Func<MlInferenceResult>> results = new();

    public ConcurrentQueue<MlWindowInferenceRequest> Requests { get; } = new();

    public int CallCount => Requests.Count;

    public void Reset()
    {
        Requests.Clear();
        while (results.TryDequeue(out _))
        {
        }
    }

    public void Enqueue(MlInferenceResult result) => results.Enqueue(() => result);

    public void Enqueue(Func<MlInferenceResult> factory) => results.Enqueue(factory);

    public void EnqueueSuccess(
        int prediction = 0,
        double supportProbability = 0.2,
        double threshold = 0.3,
        string modelVersion = "v0.1.0",
        string target = "target_support_requested") =>
        Enqueue(MlInferenceResult.Success(new MlInferenceResponse(
            prediction, supportProbability, threshold, modelVersion, target)));

    public void EnqueueFailure(MlInferenceFailureKind kind) =>
        Enqueue(MlInferenceResult.Failure(kind));

    public void EnqueueThrow(Exception exception) =>
        Enqueue(() => throw exception);

    public Task<MlInferenceResult> PredictWindowAsync(
        MlWindowInferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Enqueue(request);
        if (results.TryDequeue(out var factory))
        {
            return Task.FromResult(factory());
        }

        return Task.FromResult(MlInferenceResult.Success(new MlInferenceResponse(
            0, 0.1, 0.3, "v0.1.0", "target_support_requested")));
    }
}