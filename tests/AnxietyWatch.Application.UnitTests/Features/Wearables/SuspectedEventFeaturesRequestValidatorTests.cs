using AnxietyWatch.Application.Features.Wearables;
using FluentValidation.TestHelper;
using Xunit;

namespace AnxietyWatch.Application.UnitTests.Features.Wearables;

public sealed class SuspectedEventFeaturesRequestValidatorTests
{
    private readonly SuspectedEventFeaturesRequestValidator _validator = new();

    [Fact]
    public void PositiveSlope_IsAccepted()
    {
        var request = new SuspectedEventFeaturesRequest(
            HeartRateMean: 85.0,
            HeartRateMax: 102.0,
            HeartRateSlopeBpmPerMinute: 12.5,
            HeartRateDeltaFromBaseline: 18.0,
            RmssdMillis: 18.0,
            SdnnMillis: 25.0,
            MovementMagnitudeMean: 0.08,
            MovementVariance: 0.0012,
            ValidSampleRatio: 0.92,
            LastSampleAgeSeconds: 3,
            SampleCount: 45);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(f => f.HeartRateSlopeBpmPerMinute);
    }

    [Fact]
    public void ZeroSlope_IsAccepted()
    {
        var request = new SuspectedEventFeaturesRequest(
            HeartRateMean: 85.0,
            HeartRateMax: 102.0,
            HeartRateSlopeBpmPerMinute: 0.0,
            HeartRateDeltaFromBaseline: 18.0,
            RmssdMillis: 18.0,
            SdnnMillis: 25.0,
            MovementMagnitudeMean: 0.08,
            MovementVariance: 0.0012,
            ValidSampleRatio: 0.92,
            LastSampleAgeSeconds: 3,
            SampleCount: 45);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(f => f.HeartRateSlopeBpmPerMinute);
    }

    [Fact]
    public void NegativeSlope_IsAccepted()
    {
        var request = new SuspectedEventFeaturesRequest(
            HeartRateMean: 85.0,
            HeartRateMax: 102.0,
            HeartRateSlopeBpmPerMinute: -5.5,
            HeartRateDeltaFromBaseline: -18.0,
            RmssdMillis: 18.0,
            SdnnMillis: 25.0,
            MovementMagnitudeMean: 0.08,
            MovementVariance: 0.0012,
            ValidSampleRatio: 0.92,
            LastSampleAgeSeconds: 3,
            SampleCount: 45);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(f => f.HeartRateSlopeBpmPerMinute);
    }

    [Fact]
    public void NullSlope_IsAccepted()
    {
        var request = new SuspectedEventFeaturesRequest(
            HeartRateMean: 85.0,
            HeartRateMax: 102.0,
            HeartRateSlopeBpmPerMinute: null,
            HeartRateDeltaFromBaseline: 18.0,
            RmssdMillis: 18.0,
            SdnnMillis: 25.0,
            MovementMagnitudeMean: 0.08,
            MovementVariance: 0.0012,
            ValidSampleRatio: 0.92,
            LastSampleAgeSeconds: 3,
            SampleCount: 45);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(f => f.HeartRateSlopeBpmPerMinute);
    }

    [Fact]
    public void PositiveRmssd_IsAccepted()
    {
        var request = new SuspectedEventFeaturesRequest(
            HeartRateMean: 85.0,
            HeartRateMax: 102.0,
            HeartRateSlopeBpmPerMinute: 12.5,
            HeartRateDeltaFromBaseline: 18.0,
            RmssdMillis: 18.0,
            SdnnMillis: 25.0,
            MovementMagnitudeMean: 0.08,
            MovementVariance: 0.0012,
            ValidSampleRatio: 0.92,
            LastSampleAgeSeconds: 3,
            SampleCount: 45);

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(f => f.RmssdMillis);
    }

    [Fact]
    public void NegativeRmssd_IsRejected()
    {
        var request = new SuspectedEventFeaturesRequest(
            HeartRateMean: 85.0,
            HeartRateMax: 102.0,
            HeartRateSlopeBpmPerMinute: 12.5,
            HeartRateDeltaFromBaseline: 18.0,
            RmssdMillis: -1.0,
            SdnnMillis: 25.0,
            MovementMagnitudeMean: 0.08,
            MovementVariance: 0.0012,
            ValidSampleRatio: 0.92,
            LastSampleAgeSeconds: 3,
            SampleCount: 45);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(f => f.RmssdMillis);
    }

    [Fact]
    public void NegativeSdnn_IsRejected()
    {
        var request = new SuspectedEventFeaturesRequest(
            HeartRateMean: 85.0,
            HeartRateMax: 102.0,
            HeartRateSlopeBpmPerMinute: 12.5,
            HeartRateDeltaFromBaseline: 18.0,
            RmssdMillis: 18.0,
            SdnnMillis: -1.0,
            MovementMagnitudeMean: 0.08,
            MovementVariance: 0.0012,
            ValidSampleRatio: 0.92,
            LastSampleAgeSeconds: 3,
            SampleCount: 45);

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(f => f.SdnnMillis);
    }
}