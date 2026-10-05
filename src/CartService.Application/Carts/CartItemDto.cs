namespace CartService.Application.Carts;

using CartService.Domain;

public sealed class CartItemDto
{
    public required string ProductId { get; init; }

    public required string ProductName { get; init; }

    public required decimal UnitPrice { get; init; }

    public required int Quantity { get; init; }

    public required decimal LineTotal { get; init; }

    public static CartItemDto From(CartItem item)
    {
        return new CartItemDto
        {
            ProductId = item.ProductId,
            ProductName = item.ProductName,
            UnitPrice = item.UnitPrice.Amount,
            Quantity = item.Quantity,
            LineTotal = item.LineTotal.Amount,
        };
    }
}
