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

    public const string ProductNotFound = "cart.product_not_found";

    public const string Empty = "cart.empty";

    public const string CheckoutRequiresCustomer = "cart.checkout_requires_customer";

    public const string MergeRequiresCustomerCart = "cart.merge_requires_customer_cart";

    public const string MergeSourceNotGuest = "cart.merge_source_not_guest";
}
