namespace CartService.Domain;

/// <summary>
/// Stable codes of business rule violations. Clients can rely on them; the messages can change.
/// </summary>
public static class CartErrorCodes
{
    public const string NotActive = "cart.not_active";

    public const string QuantityOutOfRange = "cart.quantity_out_of_range";

    public const string ItemLimitExceeded = "cart.item_limit_exceeded";

    public const string ItemNotFound = "cart.item_not_found";
}
