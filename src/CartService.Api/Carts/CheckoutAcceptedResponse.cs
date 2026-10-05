namespace CartService.Api.Carts;

using System;

internal sealed class CheckoutAcceptedResponse
{
    /// <summary>Identifies this checkout. The order that the Order service creates carries the same id.</summary>
    public required Guid CheckoutId { get; init; }
}
