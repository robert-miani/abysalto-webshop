namespace CartService.Application.Events;

public sealed class CartCheckedOutItem
{
    public required string ProductId { get; init; }

    public required string ProductName { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitPrice { get; init; }
}
