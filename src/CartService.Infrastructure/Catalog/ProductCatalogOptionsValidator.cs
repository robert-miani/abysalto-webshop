namespace CartService.Infrastructure.Catalog;

using System;
using System.Linq;
using Microsoft.Extensions.Options;

/// <summary>
/// Checks what data annotations cannot: the catalog has products, and no product id appears twice.
/// </summary>
internal sealed class ProductCatalogOptionsValidator : IValidateOptions<ProductCatalogOptions>
{
    public ValidateOptionsResult Validate(string? name, ProductCatalogOptions options)
    {
        if (options.Products.Count == 0)
        {
            return ValidateOptionsResult.Fail("The product catalog must contain at least one product.");
        }

        string? duplicate = options.Products
            .GroupBy(product => product.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();

        if (duplicate is not null)
        {
            return ValidateOptionsResult.Fail($"The product id '{duplicate}' appears more than once in the product catalog.");
        }

        return ValidateOptionsResult.Success;
    }
}
