using System.Text.Json;
using System.Text.Json.Nodes;
using AnxietyWatch.Application.Abstractions.MlInference;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class MlInferenceSerializationTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void RequestContract_SerializesExactPropertyNames()
    {
        var request = new MlWindowInferenceRequest(
            EventId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            new DateTimeOffset(2026, 8, 20, 2, 0, 0, TimeSpan.FromHours(2)),
            new[]
            {
                new MlWindowSampleRequest(
                    new DateTimeOffset(2026, 8, 20, 2, 0, 10, TimeSpan.FromHours(2)),
                    72.5,
                    new[] { 810.0 },
                    33.2,
                    new MlWindowQualityRequest("good", "fair", "onBody"))
            });

        var json = JsonSerializer.Serialize(request);
        var root = JsonNode.Parse(json)!.AsObject();

        root.Select(pair => pair.Key).Should().Equal("eventId", "deviceId", "sessionId", "detectedAt", "samples");
        var sample = root["samples"]![0]!.AsObject();
        sample.Select(pair => pair.Key).Should().Equal(
            "timestamp", "heartRateBpm", "ibiMs", "skinTemperatureCelsius", "quality");
        sample["quality"]!.AsObject().Select(pair => pair.Key).Should().Equal("heartRate", "ibi", "wearingState");
    }

    [Fact]
    public void ResponseContract_DeserializesSnakeCaseProperties()
    {
        const string payload =
            """{"prediction":1,"support_probability":0.9123,"threshold":0.0032,"model_version":"0.2.1","target":"target_support_requested"}""";

        var response = JsonSerializer.Deserialize<MlInferenceResponse>(payload);

        response!.Prediction.Should().Be(1);
        response.SupportProbability.Should().Be(0.9123);
        response.Threshold.Should().Be(0.0032);
        response.ModelVersion.Should().Be("0.2.1");
        response.Target.Should().Be("target_support_requested");
    }

    [Fact]
    public void ResponseContract_RequiresExplicitSnakeCaseMapping()
    {
        const string payload =
            """{"prediction":0,"support_probability":0.5,"threshold":0.3,"model_version":"0.1.0","target":"target_support_requested"}""";

        var response = JsonSerializer.Deserialize<MlInferenceResponse>(payload);

        response!.SupportProbability.Should().Be(0.5);
        response.ModelVersion.Should().Be("0.1.0");
    }
}