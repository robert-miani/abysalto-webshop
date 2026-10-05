namespace CartService.Domain;

public enum CartStatus
{
    /// <summary>The cart can be changed.</summary>
    Active = 0,

    /// <summary>Checkout was requested. The cart is read-only until the Order service answers.</summary>
    CheckoutPending = 1,

    /// <summary>A guest cart whose items were merged into a customer cart.</summary>
    Merged = 2,
}
