namespace CartService.Application.Abstractions;

using System;
using System.Security.Cryptography;
using System.Text;
using CartService.Application.Carts;
using CartService.Domain;

/// <summary>
/// A cart as it is kept in the cache: the answer for the client together with the owner, because a cache hit
/// must not skip the check of who may see the cart.
/// </summary>
public sealed class CachedCart
{
    public required CartDto Cart { get; init; }

    public Guid? CustomerId { get; init; }

    public string? GuestTokenHash { get; init; }

    public static CachedCart From(Cart cart, CartDto dto)
    {
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(dto);

        return new CachedCart { Cart = dto, CustomerId = cart.CustomerId, GuestTokenHash = cart.GuestTokenHash };
    }

    /// <summary>The same rule as <see cref="Requester.Owns"/>, applied to the cached owner.</summary>
    public bool IsOwnedBy(Requester requester)
    {
        ArgumentNullException.ThrowIfNull(requester);

        if (requester.CustomerId.HasValue)
        {
            return CustomerId.HasValue && CustomerId.Value == requester.CustomerId.Value;
        }

        if (GuestTokenHash is null || requester.GuestTokenHash is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(GuestTokenHash),
            Encoding.UTF8.GetBytes(requester.GuestTokenHash));
    }
}
