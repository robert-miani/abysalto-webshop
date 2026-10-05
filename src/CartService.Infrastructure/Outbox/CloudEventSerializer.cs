namespace CartService.Infrastructure.Outbox;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CartService.Application.Events;

/// <summary>
/// Writes an event as a CloudEvents 1.0 document in structured JSON mode, the envelope that all services of the
/// platform use for events.
/// </summary>
internal static class CloudEventSerializer
{
    public const string Source = "/services/cart";

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(IntegrationEvent integrationEvent)
    {
        Dictionary<string, object> envelope = new Dictionary<string, object>
        {
            ["specversion"] = "1.0",
            ["id"] = integrationEvent.EventId,
            ["source"] = Source,
            ["type"] = integrationEvent.Type,
            ["subject"] = integrationEvent.OrderingKey,
            ["time"] = integrationEvent.OccurredAt,
            ["datacontenttype"] = "application/json",
            ["data"] = JsonSerializer.SerializeToElement(integrationEvent, integrationEvent.GetType(), Options),
        };

        return JsonSerializer.Serialize(envelope, Options);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        DefaultJsonTypeInfoResolver resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(HideEnvelopeProperties);

        return new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver };
    }

    /// <summary>
    /// The type and the ordering key of an event are written in the envelope (CloudEvents type and subject), so
    /// they are removed from the data of every event type, including events that are added later.
    /// </summary>
    private static void HideEnvelopeProperties(JsonTypeInfo typeInfo)
    {
        if (!typeof(IntegrationEvent).IsAssignableFrom(typeInfo.Type))
        {
            return;
        }

        string type = JsonNamingPolicy.CamelCase.ConvertName(nameof(IntegrationEvent.Type));
        string orderingKey = JsonNamingPolicy.CamelCase.ConvertName(nameof(IntegrationEvent.OrderingKey));

        for (int index = typeInfo.Properties.Count - 1; index >= 0; index--)
        {
            string name = typeInfo.Properties[index].Name;

            if (name == type || name == orderingKey)
            {
                typeInfo.Properties.RemoveAt(index);
            }
        }
    }
}
