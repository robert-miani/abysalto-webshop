namespace CartService.Domain;

public static class CartLimits
{
    /// <summary>The largest quantity of one product in a cart.</summary>
    public const int MaxQuantityPerItem = 20;

    /// <summary>The largest number of different products in a cart.</summary>
    public const int MaxDistinctItems = 50;
}
