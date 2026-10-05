namespace CartService.Application.Carts;

using System;

public sealed class CheckoutResult
{
    /// <summary>Identifies this checkout. The Order service uses the same id for the order it creates.</summary>
    public required Guid CheckoutId { get; init; }
}
