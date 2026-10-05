namespace CartService.Api.IntegrationTests.Observability;

using System.Diagnostics;
using CartService.Api.Observability;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class DropOrphanClientSpansSamplerTests
{
    private readonly DropOrphanClientSpansSampler _sampler = new DropOrphanClientSpansSampler();

    [Fact]
    public void ADatabaseCallWithoutATraceIsDropped()
    {
        _sampler.ShouldSample(Parameters(ActivityKind.Client, hasParent: false)).Decision.ShouldBe(SamplingDecision.Drop);
    }

    [Fact]
    public void ADatabaseCallInsideATraceIsKept()
    {
        _sampler.ShouldSample(Parameters(ActivityKind.Client, hasParent: true)).Decision.ShouldBe(SamplingDecision.RecordAndSample);
    }

    [Theory]
    [InlineData(ActivityKind.Server)]
    [InlineData(ActivityKind.Producer)]
    [InlineData(ActivityKind.Internal)]
    public void WorkThatStartsATraceIsKept(ActivityKind kind)
    {
        _sampler.ShouldSample(Parameters(kind, hasParent: false)).Decision.ShouldBe(SamplingDecision.RecordAndSample);
    }

    [Fact]
    public void ACallInsideATraceThatWasNotSampledIsNotRecorded()
    {
        ActivityContext parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.None);

        SamplingResult result = _sampler.ShouldSample(new SamplingParameters(parent, parent.TraceId, "SELECT", ActivityKind.Client));

        result.Decision.ShouldBe(SamplingDecision.Drop);
    }

    private static SamplingParameters Parameters(ActivityKind kind, bool hasParent)
    {
        ActivityTraceId traceId = ActivityTraceId.CreateRandom();
        ActivityContext parent = hasParent
            ? new ActivityContext(traceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded)
            : default;

        return new SamplingParameters(parent, traceId, "SELECT", kind);
    }
}
