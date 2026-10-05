namespace CartService.Domain;

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// A shopping cart. It belongs either to a signed-in customer or to a guest who is identified by a secret
/// token; only the hash of that token is stored.
/// </summary>
public sealed class Cart
{
    private readonly List<CartItem> _items = new List<CartItem>();

    private Cart(Guid id, Guid? customerId, string? guestTokenHash, DateTimeOffset now)
    {
        Id = id;
        CustomerId = customerId;
        GuestTokenHash = guestTokenHash;
        Status = CartStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; }

    public Guid? CustomerId { get; }

    public string? GuestTokenHash { get; }

    public CartStatus Status { get; private set; }

    public IReadOnlyCollection<CartItem> Items => _items;

    public Guid? CheckoutId { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Counts every change of the cart. Persistence uses it to detect two writers changing the same cart.
    /// </summary>
    public int Version { get; private set; }

    public Money Total
    {
        get
        {
            Money total = Money.Zero(Money.Euro);

            foreach (CartItem item in _items)
            {
                total = total.Add(item.LineTotal);
            }

            return total;
        }
    }

    public static Cart CreateForCustomer(Guid customerId, DateTimeOffset now)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("The customer id must not be empty.", nameof(customerId));
        }

        return new Cart(Guid.CreateVersion7(), customerId, null, now);
    }

    public static Cart CreateForGuest(string guestTokenHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guestTokenHash);

        return new Cart(Guid.CreateVersion7(), null, guestTokenHash, now);
    }

    public bool IsOwnedByCustomer(Guid customerId)
    {
        return CustomerId.HasValue && CustomerId.Value == customerId;
    }

    public bool IsOwnedByGuest(string guestTokenHash)
    {
        if (GuestTokenHash is null || guestTokenHash is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(GuestTokenHash),
            Encoding.UTF8.GetBytes(guestTokenHash));
    }
}
