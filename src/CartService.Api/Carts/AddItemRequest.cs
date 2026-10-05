namespace CartService.Api.Carts;

using System.ComponentModel.DataAnnotations;
using CartService.Domain;

/// <remarks>
/// This type must be public: the .NET 10 validation source generator silently skips internal types, and the
/// attributes below would then never run.
/// </remarks>
public sealed class AddItemRequest
{
    /// <summary>The product to add. The name and price always come from the catalog, never from the client.</summary>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string ProductId { get; init; } = string.Empty;

    /// <summary>How many units to add. A cart holds at most 20 units of one product.</summary>
    [Range(1, CartLimits.MaxQuantityPerItem)]
    public int Quantity { get; init; }
}
