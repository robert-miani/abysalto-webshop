namespace CartService.Infrastructure.Outbox;

using System.Diagnostics;
using System.Text.Json;

/// <summary>
/// Carries the trace of a request through the outbox. The event is written during the request and published
/// later by the relay, in another thread and maybe another instance, so the trace is stored in the event itself
/// with the CloudEvents distributed tracing extension (<c>traceparent</c> and <c>tracestate</c>). The Order
/// service reads the same attributes and continues the trace, so one trace shows the checkout request, the
/// publishing, and the order that follows.
/// </summary>
internal static class OutboxTraceContext
{
    public const string TraceParentAttribute = "traceparent";
    public const string TraceStateAttribute = "tracestate";

    /// <summary>Reads the trace context of an event, or returns false when the event has none.</summary>
    public static bool TryRead(string payload, out ActivityContext context)
    {
        context = default;

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(TraceParentAttribute, out JsonElement traceParent)
                || traceParent.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            string? traceState = document.RootElement.TryGetProperty(TraceStateAttribute, out JsonElement state) && state.ValueKind == JsonValueKind.String
                ? state.GetString()
                : null;

            return ActivityContext.TryParse(traceParent.GetString(), traceState, isRemote: true, out context);
        }
        catch (JsonException)
        {
            // A payload that is not JSON cannot carry a trace. Publishing it is not the business of this class.
            return false;
        }
    }
}
