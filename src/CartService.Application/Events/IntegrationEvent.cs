namespace CartService.Application.Events;

using System;

/// <summary>
/// A fact about the cart that other services of the platform care about. Integration events are written to the
/// outbox in the same transaction as the change they describe, and published afterwards.
/// </summary>
public abstract class IntegrationEvent
{
    /// <summary>Identifies the event. Consumers use it to skip an event they have already processed.</summary>
    public required Guid EventId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The name of the event type, for example <c>CartCheckedOut</c>.</summary>
    public abstract string Type { get; }

    /// <summary>
    /// Events with the same key are processed in order by one consumer, which is the Service Bus session of the
    /// message. For a checkout it is the checkout id.
    /// </summary>
    public abstract string OrderingKey { get; }
}
