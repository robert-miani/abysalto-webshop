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

    /// <summary>
    /// Adds units of a product. When the product is already in the cart, the quantities are added and the line
    /// takes the name and price that were just read from the catalog.
    /// </summary>
    public void AddItem(string productId, string productName, Money unitPrice, int quantity, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentNullException.ThrowIfNull(unitPrice);
        EnsureEuro(unitPrice);
        EnsureActive();
        EnsureValidQuantity(quantity);

        CartItem? existing = Find(productId);

        if (existing is null)
        {
            if (_items.Count >= CartLimits.MaxDistinctItems)
            {
                throw new CartRuleViolationException(
                    CartErrorCodes.ItemLimitExceeded,
                    $"A cart can hold at most {CartLimits.MaxDistinctItems} different products.");
            }

            _items.Add(new CartItem(productId, productName, unitPrice, quantity));
        }
        else
        {
            int newQuantity = existing.Quantity + quantity;
            EnsureValidQuantity(newQuantity);
            existing.Update(productName, unitPrice, newQuantity);
        }

        Touch(now);
    }

    public void ChangeQuantity(string productId, int quantity, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        EnsureActive();
        EnsureValidQuantity(quantity);

        CartItem item = Find(productId)
            ?? throw new CartRuleViolationException(CartErrorCodes.ItemNotFound, $"Product '{productId}' is not in the cart.");

        item.SetQuantity(quantity);
        Touch(now);
    }

    /// <summary>
    /// Removes a product. Removing a product that is not in the cart changes nothing.
    /// </summary>
    public void RemoveItem(string productId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        EnsureActive();

        CartItem? item = Find(productId);

        if (item is null)
        {
            return;
        }

        _items.Remove(item);
        Touch(now);
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

    private static void EnsureValidQuantity(int quantity)
    {
        if (quantity < 1 || quantity > CartLimits.MaxQuantityPerItem)
        {
            throw new CartRuleViolationException(
                CartErrorCodes.QuantityOutOfRange,
                $"The quantity of a product must be between 1 and {CartLimits.MaxQuantityPerItem}.");
        }
    }

    private static void EnsureEuro(Money price)
    {
        if (!string.Equals(price.Currency, Money.Euro, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Prices must be in {Money.Euro}.", nameof(price));
        }
    }

    private CartItem? Find(string productId)
    {
        return _items.Find(item => string.Equals(item.ProductId, productId, StringComparison.Ordinal));
    }

    private void EnsureActive()
    {
        if (Status != CartStatus.Active)
        {
            throw new CartRuleViolationException(CartErrorCodes.NotActive, "Only an active cart can be changed.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
