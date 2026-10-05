namespace CartService.Api.IntegrationTests.Outbox;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using CartService.Application.Events;
using CartService.Infrastructure.Outbox;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class OutboxTraceContextTests : IDisposable
{
    private const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    private readonly ActivitySource _source = new ActivitySource("CartService.Tests.TraceContext");
    private readonly ActivityListener _listener;

    public OutboxTraceContextTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == _source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    [Fact]
    public void TheTraceContextOfAnEventIsRead()
    {
        string payload = $"{{\"specversion\":\"1.0\",\"traceparent\":\"{TraceParent}\",\"tracestate\":\"vendor=1\"}}";

        bool found = OutboxTraceContext.TryRead(payload, out ActivityContext context);

        found.ShouldBeTrue();
        context.TraceId.ToString().ShouldBe("0af7651916cd43dd8448eb211c80319c");
        context.SpanId.ToString().ShouldBe("b7ad6b7169203331");
        context.TraceState.ShouldBe("vendor=1");
        context.IsRemote.ShouldBeTrue();
    }

    [Theory]
    [InlineData("{\"specversion\":\"1.0\"}")]
    [InlineData("{\"traceparent\":42}")]
    [InlineData("{\"traceparent\":\"not-a-trace\"}")]
    [InlineData("[]")]
    [InlineData("this is not json")]
    [InlineData("")]
    public void AnEventWithoutAUsableTraceContextHasNone(string payload)
    {
        OutboxTraceContext.TryRead(payload, out _).ShouldBeFalse();
    }

    [Fact]
    public void AnEventWrittenInsideARequestCarriesItsTrace()
    {
        using Activity activity = _source.StartActivity("request")!;

        JsonElement envelope = Serialize();

        envelope.GetProperty("traceparent").GetString().ShouldBe(activity.Id);
        envelope.GetProperty("specversion").GetString().ShouldBe("1.0");
        OutboxTraceContext.TryRead(envelope.GetRawText(), out ActivityContext context).ShouldBeTrue();
        context.TraceId.ShouldBe(activity.TraceId);
    }

    [Fact]
    public void ATraceStateIsCarriedToo()
    {
        using Activity activity = _source.StartActivity("request")!;
        activity.TraceStateString = "vendor=abc";

        JsonElement envelope = Serialize();

        envelope.GetProperty("tracestate").GetString().ShouldBe("vendor=abc");
    }

    [Fact]
    public void AnEventWrittenOutsideAnyActivityHasNoTrace()
    {
        Activity.Current = null;

        JsonElement envelope = Serialize();

        envelope.TryGetProperty("traceparent", out _).ShouldBeFalse();
        envelope.TryGetProperty("tracestate", out _).ShouldBeFalse();
    }

    private static JsonElement Serialize()
    {
        CartCheckedOut integrationEvent = new CartCheckedOut
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            CheckoutId = Guid.NewGuid(),
            CartId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Items = new List<CartCheckedOutItem>(),
            IndicativeTotal = 0m,
            Currency = "EUR",
        };

        return JsonSerializer.Deserialize<JsonElement>(CloudEventSerializer.Serialize(integrationEvent));
    }
}
