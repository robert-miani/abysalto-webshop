namespace CartService.Infrastructure.Catalog;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

/// <summary>
/// The products of the stand-in catalog. In the platform, product data comes from the Catalog and Pricing
/// services, so this list only exists to run the Cart service on its own.
/// </summary>
public sealed class ProductCatalogOptions
{
    public const string SectionName = "ProductCatalog";

    [Required]
    [ValidateEnumeratedItems]
    public IList<ProductOptions> Products { get; init; } = new List<ProductOptions>();
}
