using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnxietyWatch.Application.Abstractions.MlInference;
using AnxietyWatch.Infrastructure.MlInference;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed record CapturedRequest(string Method, string Url, IReadOnlyDictionary<string, string> Headers, string? Body);

public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    internal const string SuccessBody =
        """{"prediction":0,"support_probability":0.001,"threshold":0.003,"model_version":"0.1.0","target":"target_support_requested"}""";

    private readonly Queue<HttpResponseMessage> responses = new();
    private readonly TimeSpan? delay;

    public FakeHttpMessageHandler(TimeSpan? delay = null)
    {
        this.delay = delay;
    }

    public bool ThrowNetworkError { get; set; }

    public List<CapturedRequest> Requests { get; } = new();

    public int AttemptCount { get; private set; }

    public void Enqueue(int statusCode, string? body = null) =>
        responses.Enqueue(new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = body is null ? null : new StringContent(body, Encoding.UTF8, "application/json")
        });

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        AttemptCount++;
        if (ThrowNetworkError)
        {
            throw new HttpRequestException("simulated network failure");
        }

        if (delay is { } wait)
        {
            await Task.Delay(wait, cancellationToken);
        }

        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(
            pair => pair.Key,
            pair => string.Join(",", pair.Value),
            StringComparer.OrdinalIgnoreCase);
        Requests.Add(new CapturedRequest(request.Method.ToString(), request.RequestUri!.ToString(), headers, body));

        return responses.Count > 0
            ? responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessBody, Encoding.UTF8, "application/json")
            };
    }
}

public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
    }
}

public sealed class MlInferenceClientTests
{
    private const string FakeApiKey = "fake-test-api-key";
    private const string BaseUrl = "https://ml.example.test";

    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static IConfiguration Config(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(entry => entry.Key, entry => entry.Value))
            .Build();

    private static IConfiguration ValidConfig() => Config(
        ("Ml:Inference:BaseUrl", BaseUrl),
        ("Ml:Inference:ApiKey", FakeApiKey),
        ("Ml:Inference:Retry:BaseDelaySeconds", "0"));

    private static MlWindowInferenceRequest SampleRequest() => new(
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

    private static (MlInferenceHttpClient Client, FakeHttpMessageHandler Handler, RecordingLogger<MlInferenceHttpClient> Logger) CreateClient(
        IConfiguration configuration,
        TimeSpan? httpTimeout = null,
        TimeSpan? handlerDelay = null)
    {
        var logger = new RecordingLogger<MlInferenceHttpClient>();
        var handler = new FakeHttpMessageHandler(handlerDelay);
        var httpClient = new HttpClient(handler)
        {
            Timeout = httpTimeout ?? TimeSpan.FromSeconds(10)
        };
        return (new MlInferenceHttpClient(logger, httpClient, configuration), handler, logger);
    }

    [Fact]
    public async Task A_RequestUrl_IsPostBaseUrlPredictWindow()
    {
        var (client, handler, _) = CreateClient(ValidConfig());

        await client.PredictWindowAsync(SampleRequest());

        handler.Requests.Should().ContainSingle().Subject.Url.Should().Be($"{BaseUrl}/predict/window");
    }

    [Fact]
    public async Task B_HttpMethod_IsPost()
    {
        var (client, handler, _) = CreateClient(ValidConfig());

        await client.PredictWindowAsync(SampleRequest());

        handler.Requests.Should().ContainSingle().Subject.Method.Should().Be("POST");
    }

    [Fact]
    public async Task C_XApiKeyHeader_CarriesConfiguredSecret()
    {
        var (client, handler, _) = CreateClient(ValidConfig());

        await client.PredictWindowAsync(SampleRequest());

        handler.Requests.Should().ContainSingle().Subject.Headers["X-Api-Key"].Should().Be(FakeApiKey);
    }

    [Fact]
    public async Task D_XCorrelationIdHeader_EqualsEventId()
    {
        var (client, handler, _) = CreateClient(ValidConfig());

        await client.PredictWindowAsync(SampleRequest());

        handler.Requests.Should().ContainSingle().Subject.Headers["X-Correlation-Id"].Should().Be(EventId.ToString());
    }

    [Fact]
    public async Task E_RequestBody_ExactCamelCaseContract()
    {
        var (client, handler, _) = CreateClient(ValidConfig());

        await client.PredictWindowAsync(SampleRequest());

        var body = handler.Requests.Should().ContainSingle().Subject.Body;
        var root = JsonNode.Parse(body!)!.AsObject();
        root.Select(pair => pair.Key).Should().Equal("eventId", "deviceId", "sessionId", "detectedAt", "samples");
        root["eventId"]!.GetValue<string>().Should().Be(EventId.ToString());
        root["samples"]!.AsArray().Should().HaveCount(1);
        var sample = root["samples"]![0]!.AsObject();
        sample.Select(pair => pair.Key).Should().Equal(
            "timestamp", "heartRateBpm", "ibiMs", "skinTemperatureCelsius", "quality");
        sample["heartRateBpm"]!.GetValue<double>().Should().Be(72.5);
        sample["ibiMs"]!.AsArray().Should().ContainSingle().Which.GetValue<double>().Should().Be(810.0);
        sample["skinTemperatureCelsius"]!.GetValue<double>().Should().Be(33.2);
        var quality = sample["quality"]!.AsObject();
        quality.Select(pair => pair.Key).Should().Equal("heartRate", "ibi", "wearingState");
        quality["heartRate"]!.GetValue<string>().Should().Be("good");
        quality["ibi"]!.GetValue<string>().Should().Be("fair");
        quality["wearingState"]!.GetValue<string>().Should().Be("onBody");
    }

    [Fact]
    public async Task F_RequestBody_ContainsNoUnnecessaryFields()
    {
        var (client, handler, _) = CreateClient(ValidConfig());

        await client.PredictWindowAsync(SampleRequest());

        var body = handler.Requests.Should().ContainSingle().Subject.Body!;
        body.Should().NotContain("accelerometer");
        body.Should().NotContain("ambientTemperature");
        body.Should().NotContain("DerivedFeatures");
        body.Should().NotContain("userId");
        body.Should().NotContain("baseline");
        body.Should().NotContain("features");
        body.Should().NotContain("rulesVersion");
        body.Should().NotContain(FakeApiKey);
    }

    [Fact]
    public async Task G_SnakeCaseResponse_IsParsed()
    {
        var (client, handler, _) = CreateClient(ValidConfig());
        handler.Enqueue(200, FakeHttpMessageHandler.SuccessBody);

        var result = await client.PredictWindowAsync(SampleRequest());

        result.IsSuccess.Should().BeTrue();
        result.Response!.SupportProbability.Should().Be(0.001);
        result.Response.Threshold.Should().Be(0.003);
        result.Response.ModelVersion.Should().Be("0.1.0");
        result.Response.Target.Should().Be("target_support_requested");
    }

    [Fact]
    public async Task H_PredictionZero_IsPreserved()
    {
        var (client, handler, _) = CreateClient(ValidConfig());
        handler.Enqueue(200, FakeHttpMessageHandler.SuccessBody);

        var result = await client.PredictWindowAsync(SampleRequest());

        result.IsSuccess.Should().BeTrue();
        result.Response!.Prediction.Should().Be(0);
    }

    [Fact]
    public async Task I_PredictionOne_IsPreservedWithoutSideEffects()
    {
        var (client, handler, _) = CreateClient(ValidConfig());
        handler.Enqueue(200,
            """{"prediction":1,"support_probability":0.9,"threshold":0.003,"model_version":"0.1.0","target":"target_support_requested"}""");

        var result = await client.PredictWindowAsync(SampleRequest());

        result.IsSuccess.Should().BeTrue();
        result.Response!.Prediction.Should().Be(1);
        result.Response.SupportProbability.Should().Be(0.9);
    }

    [Fact]
    public async Task J_MissingApiKey_IsConfigurationFailureWithZeroRequests()
    {
        var (client, handler, _) = CreateClient(Config(
            ("Ml:Inference:BaseUrl", BaseUrl),
            ("Ml:Inference:Retry:BaseDelaySeconds", "0")));

        var result = await client.PredictWindowAsync(SampleRequest());

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(MlInferenceFailureKind.Configuration);
        handler.AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task K_MissingOrInvalidBaseUrl_IsConfigurationFailureWithZeroRequests()
    {
        var missing = CreateClient(Config(
            ("Ml:Inference:ApiKey", FakeApiKey),
            ("Ml:Inference:Retry:BaseDelaySeconds", "0")));
        var missingResult = await missing.Client.PredictWindowAsync(SampleRequest());
        missingResult.FailureKind.Should().Be(MlInferenceFailureKind.Configuration);
        missing.Handler.AttemptCount.Should().Be(0);

        var invalid = CreateClient(Config(
            ("Ml:Inference:BaseUrl", "not-a-valid-url"),
            ("Ml:Inference:ApiKey", FakeApiKey),
            ("Ml:Inference:Retry:BaseDelaySeconds", "0")));
        var invalidResult = await invalid.Client.PredictWindowAsync(SampleRequest());
        invalidResult.FailureKind.Should().Be(MlInferenceFailureKind.Configuration);
        invalid.Handler.AttemptCount.Should().Be(0);
    }

    [Theory]
    [InlineData(401, MlInferenceFailureKind.Unauthorized)]
    [InlineData(403, MlInferenceFailureKind.Unauthorized)]
    public async Task UnauthorizedStatuses_AreClassifiedWithoutRetry(int statusCode, MlInferenceFailureKind expected)
    {
        var (client, handler, _) = CreateClient(ValidConfig());
        handler.Enqueue(statusCode);

        var result = await client.PredictWindowAsync(SampleRequest());

        result.FailureKind.Should().Be(expected);
        handler.AttemptCount.Should().Be(1);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(422)]
    public async Task ValidationStatuses_AreClassifiedWithoutRetry(int statusCode)
    {
        var (client, handler, _) = CreateClient(ValidConfig());
        handler.Enqueue(statusCode);

        var result = await client.PredictWindowAsync(SampleRequest());

        result.FailureKind.Should().Be(MlInferenceFailureKind.Validation);
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task O_TransientStatus_RetriesTwiceThenFails()
    {
        var (client, handler, _) = CreateClient(ValidConfig());
        handler.Enqueue(503);
        handler.Enqueue(503);
        handler.Enqueue(503);

        var result = await client.PredictWindowAsync(SampleRequest());

        result.FailureKind.Should().Be(MlInferenceFailureKind.Transient);
        handler.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task P1_Timeout_IsTransientAndRetried()
    {
        var (client, handler, _) = CreateClient(ValidConfig(), httpTimeout: TimeSpan.FromMilliseconds(100), handlerDelay: TimeSpan.FromSeconds(30));

        var result = await client.PredictWindowAsync(SampleRequest());

        result.FailureKind.Should().Be(MlInferenceFailureKind.Transient);
        handler.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task P2_NetworkFailure_IsTransientAndRetried()
    {
        var (client, handler, _) = CreateClient(ValidConfig());
        handler.ThrowNetworkError = true;

        var result = await client.PredictWindowAsync(SampleRequest());

        result.FailureKind.Should().Be(MlInferenceFailureKind.Transient);
        handler.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task Q_TransientThenSuccess_RecoversOnRetry()
    {
        var (client, handler, _) = CreateClient(ValidConfig());
        handler.Enqueue(503);

        var result = await client.PredictWindowAsync(SampleRequest());

        result.IsSuccess.Should().BeTrue();
        result.Response!.ModelVersion.Should().Be("0.1.0");
        handler.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task R_MalformedSuccessPayload_IsUnexpectedWithoutCrash()
    {
        var (client, handler, _) = CreateClient(ValidConfig());
        handler.Enqueue(200, "not-json");

        var result = await client.PredictWindowAsync(SampleRequest());

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task S_Secret_NeverAppearsInLogsErrorsOrBody()
    {
        var (client, handler, logger) = CreateClient(ValidConfig());
        handler.Enqueue(503);
        handler.Enqueue(503);
        handler.Enqueue(503);
        var failing = await client.PredictWindowAsync(SampleRequest());
        failing.FailureKind.Should().Be(MlInferenceFailureKind.Transient);

        await client.PredictWindowAsync(SampleRequest());

        logger.Messages.Should().NotContain(message => message.Contains(FakeApiKey, StringComparison.Ordinal));
        handler.Requests.Should().OnlyContain(request => request.Body == null || !request.Body.Contains(FakeApiKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task T_NullableRawValues_SerializeCorrectly()
    {
        var (client, handler, _) = CreateClient(ValidConfig());
        var request = new MlWindowInferenceRequest(
            EventId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            new DateTimeOffset(2026, 8, 20, 2, 0, 0, TimeSpan.FromHours(2)),
            new[]
            {
                new MlWindowSampleRequest(
                    new DateTimeOffset(2026, 8, 20, 2, 0, 10, TimeSpan.FromHours(2)),
                    null,
                    Array.Empty<double>(),
                    null,
                    new MlWindowQualityRequest("unknown", "unknown", "unknown"))
            });

        await client.PredictWindowAsync(request);

        var body = handler.Requests.Should().ContainSingle().Subject.Body;
        using var document = JsonDocument.Parse(body!);
        var sample = document.RootElement.GetProperty("samples")[0];
        sample.GetProperty("heartRateBpm").ValueKind.Should().Be(JsonValueKind.Null);
        sample.GetProperty("ibiMs").GetArrayLength().Should().Be(0);
        sample.GetProperty("skinTemperatureCelsius").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task HttpsBaseUrl_IsAccepted()
    {
        var (client, handler, _) = CreateClient(ValidConfig());

        var result = await client.PredictWindowAsync(SampleRequest());

        result.IsSuccess.Should().BeTrue();
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task HttpBaseUrl_IsRejectedAsConfigurationFailureWithZeroRequests()
    {
        var (client, handler, _) = CreateClient(Config(
            ("Ml:Inference:BaseUrl", "http://ml.example.test"),
            ("Ml:Inference:ApiKey", FakeApiKey),
            ("Ml:Inference:Retry:BaseDelaySeconds", "0")));

        var result = await client.PredictWindowAsync(SampleRequest());

        result.FailureKind.Should().Be(MlInferenceFailureKind.Configuration);
        handler.AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task Redirect3xx_IsUnexpectedWithoutRetry()
    {
        var (client, handler, _) = CreateClient(ValidConfig());
        handler.Enqueue(302);

        var result = await client.PredictWindowAsync(SampleRequest());

        result.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
        handler.AttemptCount.Should().Be(1);
    }

    private static async Task<(MlInferenceResult Result, FakeHttpMessageHandler Handler)> PredictWithBodyAsync(string body)
    {
        var (client, handler, _) = CreateClient(ValidConfig());
        handler.Enqueue(200, body);
        var result = await client.PredictWindowAsync(SampleRequest());
        return (result, handler);
    }

    [Fact]
    public async Task A_EmptySuccessObject_IsUnexpectedWithoutRetry()
    {
        var (result, handler) = await PredictWithBodyAsync("{}");
        result.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task B_MissingSupportProbability_IsUnexpectedWithoutRetry()
    {
        var (result, handler) = await PredictWithBodyAsync(
            """{"prediction":0,"threshold":0.003,"model_version":"0.1.0","target":"target_support_requested"}""");
        result.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task C_MissingThreshold_IsUnexpectedWithoutRetry()
    {
        var (result, handler) = await PredictWithBodyAsync(
            """{"prediction":0,"support_probability":0.001,"model_version":"0.1.0","target":"target_support_requested"}""");
        result.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task D_MissingModelVersion_IsUnexpectedWithoutRetry()
    {
        var (result, handler) = await PredictWithBodyAsync(
            """{"prediction":0,"support_probability":0.001,"threshold":0.003,"target":"target_support_requested"}""");
        result.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task E_MissingTarget_IsUnexpectedWithoutRetry()
    {
        var (result, handler) = await PredictWithBodyAsync(
            """{"prediction":0,"support_probability":0.001,"threshold":0.003,"model_version":"0.1.0"}""");
        result.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task F_PredictionOutsideRange_IsUnexpectedWithoutRetry()
    {
        var (result, handler) = await PredictWithBodyAsync(
            """{"prediction":2,"support_probability":0.5,"threshold":0.3,"model_version":"0.1.0","target":"target_support_requested"}""");
        result.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task G_NegativeSupportProbability_IsUnexpectedWithoutRetry()
    {
        var (result, handler) = await PredictWithBodyAsync(
            """{"prediction":0,"support_probability":-0.1,"threshold":0.3,"model_version":"0.1.0","target":"target_support_requested"}""");
        result.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task H_SupportProbabilityAboveOne_IsUnexpectedWithoutRetry()
    {
        var (result, handler) = await PredictWithBodyAsync(
            """{"prediction":0,"support_probability":1.1,"threshold":0.3,"model_version":"0.1.0","target":"target_support_requested"}""");
        result.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task I_ThresholdOutsideRange_IsUnexpectedWithoutRetry()
    {
        var (result, handler) = await PredictWithBodyAsync(
            """{"prediction":0,"support_probability":0.5,"threshold":1.2,"model_version":"0.1.0","target":"target_support_requested"}""");
        result.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task J_EmptyModelVersion_IsUnexpectedWithoutRetry()
    {
        var (result, handler) = await PredictWithBodyAsync(
            """{"prediction":0,"support_probability":0.5,"threshold":0.3,"model_version":"","target":"target_support_requested"}""");
        result.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task K_WrongTarget_IsUnexpectedWithoutRetry()
    {
        var (result, handler) = await PredictWithBodyAsync(
            """{"prediction":0,"support_probability":0.5,"threshold":0.3,"model_version":"0.1.0","target":"another_target"}""");
        result.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
        handler.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task L_FullyValidResponse_StillSucceedsWithoutRetry()
    {
        var (result, handler) = await PredictWithBodyAsync(FakeHttpMessageHandler.SuccessBody);
        result.IsSuccess.Should().BeTrue();
        result.Response!.SupportProbability.Should().Be(0.001);
        result.Response.ModelVersion.Should().Be("0.1.0");
        handler.AttemptCount.Should().Be(1);
    }
}