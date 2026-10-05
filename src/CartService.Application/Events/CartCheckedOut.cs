namespace CartService.Application.Events;

using System;
using System.Collections.Generic;

/// <summary>
/// A customer started checkout. The Order service reacts to this event and starts the checkout saga.
/// </summary>
/// <remarks>
/// The prices are a snapshot of what the customer saw. The total is only indicative: the Order service asks the
/// Pricing service for the final price before it reserves stock.
/// </remarks>
public sealed class CartCheckedOut : IntegrationEvent
{
    public const string EventType = "CartCheckedOut";

    public required Guid CheckoutId { get; init; }

    public required Guid CartId { get; init; }

    public required Guid CustomerId { get; init; }

    public required IReadOnlyList<CartCheckedOutItem> Items { get; init; }

    public required decimal IndicativeTotal { get; init; }

    public required string Currency { get; init; }

    public override string Type => EventType;

    public override string OrderingKey => CheckoutId.ToString();
}
