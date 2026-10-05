namespace CartService.Application.Carts;

public sealed class CreateCartResult
{
    public required CartDto Cart { get; init; }

    /// <summary>True when this call created the cart, false when the customer already had an active cart.</summary>
    public required bool Created { get; init; }

    /// <summary>The secret of a new guest cart. It is only returned here, once.</summary>
    public string? GuestToken { get; init; }
}
