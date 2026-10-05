namespace CartService.Infrastructure.Catalog;

using System.ComponentModel.DataAnnotations;

public sealed class ProductOptions
{
    [Required]
    [StringLength(64)]
    public string Id { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;

    /// <summary>The unit price in euro.</summary>
    [Range(0.01, 100000)]
    public decimal Price { get; init; }
}
