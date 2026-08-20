using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AnxietyWatch.Application.Abstractions.MlInference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.Infrastructure.MlInference;

public sealed class MlInferenceHttpClient(
    ILogger<MlInferenceHttpClient> logger,
    HttpClient httpClient,
    IConfiguration configuration) : IMlInferenceClient
{
    private readonly string? _baseUrl = configuration["Ml:Inference:BaseUrl"];
    private readonly string? _apiKey = configuration["Ml:Inference:ApiKey"];
    private readonly double _baseDelaySeconds = ParseDouble(configuration["Ml:Inference:Retry:BaseDelaySeconds"], 1);
    private readonly int _maxRetries = ParseInt(configuration["Ml:Inference:Retry:MaxRetries"], 2);

    public async Task<MlInferenceResult> PredictWindowAsync(
        MlWindowInferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveConfiguration(out var baseUrl))
        {
            logger.LogWarning(
                "ML inference configuration is incomplete; refusing to call ML for event {EventId}.",
                request.EventId);
            return MlInferenceResult.Failure(MlInferenceFailureKind.Configuration);
        }

        var attempt = 0;
        while (true)
        {
            var latency = Stopwatch.StartNew();
            MlInferenceResult result;
            try
            {
                using var httpRequest = BuildRequest(baseUrl, request);
                using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
                latency.Stop();
                result = await HandleResponseAsync(response, latency.Elapsed, request, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                latency.Stop();
                logger.LogWarning(
                    "ML inference timed out for event {EventId} after {LatencyMs}ms.",
                    request.EventId,
                    latency.ElapsedMilliseconds);
                result = MlInferenceResult.Failure(MlInferenceFailureKind.Transient);
            }
            catch (HttpRequestException)
            {
                latency.Stop();
                logger.LogWarning(
                    "ML inference request failed for event {EventId} after {LatencyMs}ms.",
                    request.EventId,
                    latency.ElapsedMilliseconds);
                result = MlInferenceResult.Failure(MlInferenceFailureKind.Transient);
            }

            if (!result.IsSuccess && result.FailureKind == MlInferenceFailureKind.Transient)
            {
                if (attempt >= _maxRetries)
                {
                    logger.LogWarning("ML inference retries exhausted for event {EventId}.", request.EventId);
                    return result;
                }

                await Task.Delay(RetryDelay(attempt), cancellationToken);
                attempt++;
                continue;
            }

            return result;
        }
    }

    private HttpRequestMessage BuildRequest(Uri baseUrl, MlWindowInferenceRequest request)
    {
        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl.AbsoluteUri.TrimEnd('/')}/predict/window")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);
        httpRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", request.EventId.ToString());
        return httpRequest;
    }

    private async Task<MlInferenceResult> HandleResponseAsync(
        HttpResponseMessage response,
        TimeSpan latency,
        MlWindowInferenceRequest request,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            try
            {
                var parsed = await response.Content.ReadFromJsonAsync<MlInferenceResponse>(cancellationToken);
                if (parsed is null)
                {
                    logger.LogWarning(
                        "ML inference returned an empty success payload for event {EventId} after {LatencyMs}ms.",
                        request.EventId,
                        latency.TotalMilliseconds);
                    return MlInferenceResult.Failure(MlInferenceFailureKind.Unexpected);
                }

                logger.LogInformation(
                    "ML inference succeeded for event {EventId} (model {ModelVersion}) after {LatencyMs}ms.",
                    request.EventId,
                    parsed.ModelVersion,
                    latency.TotalMilliseconds);
                return MlInferenceResult.Success(parsed);
            }
            catch (JsonException)
            {
                logger.LogWarning(
                    "ML inference returned a malformed success payload for event {EventId} after {LatencyMs}ms.",
                    request.EventId,
                    latency.TotalMilliseconds);
                return MlInferenceResult.Failure(MlInferenceFailureKind.Unexpected);
            }
        }

        var kind = Classify(response.StatusCode);
        logger.LogWarning(
            "ML inference failed for event {EventId} with {FailureKind} (HTTP {StatusCode}) after {LatencyMs}ms.",
            request.EventId,
            kind,
            (int)response.StatusCode,
            latency.TotalMilliseconds);
        return MlInferenceResult.Failure(kind);
    }

    private static MlInferenceFailureKind Classify(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => MlInferenceFailureKind.Unauthorized,
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => MlInferenceFailureKind.Validation,
        HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => MlInferenceFailureKind.Transient,
        _ => MlInferenceFailureKind.Unexpected
    };

    private bool TryResolveConfiguration(out Uri baseUrl)
    {
        baseUrl = null!;
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_baseUrl) ||
            !Uri.TryCreate(_baseUrl, UriKind.Absolute, out var candidate) ||
            (candidate.Scheme != Uri.UriSchemeHttps && candidate.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        baseUrl = candidate;
        return true;
    }

    private TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromSeconds(_baseDelaySeconds * Math.Pow(2, attempt));

    private static double ParseDouble(string? value, double fallback) =>
        double.TryParse(value, out var parsed) && parsed >= 0 ? parsed : fallback;

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed >= 0 ? parsed : fallback;
}