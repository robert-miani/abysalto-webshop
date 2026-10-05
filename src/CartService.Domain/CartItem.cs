namespace CartService.Domain;

using System;

/// <summary>
/// One line of a cart: a product, the price it had when the line was last touched, and a quantity.
/// </summary>
public sealed class CartItem
{
    public CartItem(string productId, string productName, Money unitPrice, int quantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentNullException.ThrowIfNull(unitPrice);

        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public string ProductId { get; }

    public string ProductName { get; private set; }

    public Money UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public Money LineTotal => UnitPrice.Multiply(Quantity);

    internal void Update(string productName, Money unitPrice, int quantity)
    {
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    internal void SetQuantity(int quantity)
    {
        Quantity = quantity;
    }
}
