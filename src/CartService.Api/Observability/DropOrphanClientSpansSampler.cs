namespace CartService.Api.Observability;

using System.Diagnostics;
using OpenTelemetry.Trace;

/// <summary>
/// Drops client spans that belong to no trace, and records everything else as its parent says. The outbox relay
/// polls the database every few seconds and the host connects and migrates at startup; without a request or a
/// background job around them, each of those calls would be a trace of its own, and they would drown the traces of
/// the requests. A database call inside a request, a published message or a cleanup run has a parent and is kept.
/// </summary>
internal sealed class DropOrphanClientSpansSampler : Sampler
{
    private readonly Sampler _parentBased = new ParentBasedSampler(new AlwaysOnSampler());

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        if (samplingParameters.Kind == ActivityKind.Client && samplingParameters.ParentContext.TraceId == default)
        {
            return new SamplingResult(SamplingDecision.Drop);
        }

        return _parentBased.ShouldSample(samplingParameters);
    }
}
