namespace CartService.Infrastructure.Catalog;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;
using Microsoft.Extensions.Options;

/// <summary>
/// Serves the products from configuration. It stands in for the Catalog and Pricing services of the platform.
/// </summary>
internal sealed class ConfiguredProductCatalog : IProductCatalog
{
    private readonly Dictionary<string, CatalogProduct> _products;

    public ConfiguredProductCatalog(IOptions<ProductCatalogOptions> options)
    {
        _products = new Dictionary<string, CatalogProduct>(StringComparer.Ordinal);

        foreach (ProductOptions product in options.Value.Products)
        {
            _products[product.Id] = new CatalogProduct
            {
                ProductId = product.Id,
                Name = product.Name,
                UnitPrice = Money.Eur(product.Price),
            };
        }
    }

    public Task<CatalogProduct?> FindAsync(string productId, CancellationToken cancellationToken)
    {
        _products.TryGetValue(productId, out CatalogProduct? product);

        return Task.FromResult(product);
    }
}
