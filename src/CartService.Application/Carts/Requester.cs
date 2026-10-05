namespace CartService.Application.Carts;

using System;
using CartService.Domain;

/// <summary>
/// Who is asking: a signed-in customer, or a guest who proves access with a cart token.
/// </summary>
public sealed class Requester
{
    private Requester(Guid? customerId, string? guestTokenHash)
    {
        CustomerId = customerId;
        GuestTokenHash = guestTokenHash;
    }

    public Guid? CustomerId { get; }

    public string? GuestTokenHash { get; }

    public static Requester ForCustomer(Guid customerId)
    {
        return new Requester(customerId, null);
    }

    public static Requester ForGuest(string guestTokenHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guestTokenHash);

        return new Requester(null, guestTokenHash);
    }

    public bool Owns(Cart cart)
    {
        ArgumentNullException.ThrowIfNull(cart);

        if (CustomerId.HasValue)
        {
            return cart.IsOwnedByCustomer(CustomerId.Value);
        }

        return GuestTokenHash is not null && cart.IsOwnedByGuest(GuestTokenHash);
    }
}
