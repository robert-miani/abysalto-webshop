namespace CartService.Api.Carts;

using System.ComponentModel.DataAnnotations;
using CartService.Domain;

/// <remarks>
/// This type must be public: the .NET 10 validation source generator silently skips internal types, and the
/// attribute below would then never run.
/// </remarks>
public sealed class ChangeQuantityRequest
{
    /// <summary>The new quantity. To remove a product, delete the line instead.</summary>
    [Range(1, CartLimits.MaxQuantityPerItem)]
    public int Quantity { get; init; }
}
