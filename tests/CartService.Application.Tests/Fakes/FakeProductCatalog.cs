namespace CartService.Application.Tests.Fakes;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;

internal sealed class FakeProductCatalog : IProductCatalog
{
    private readonly Dictionary<string, CatalogProduct> _products = new Dictionary<string, CatalogProduct>();

    public void Add(string productId, string name, decimal price)
    {
        _products[productId] = new CatalogProduct { ProductId = productId, Name = name, UnitPrice = Money.Eur(price) };
    }

    public Task<CatalogProduct?> FindAsync(string productId, CancellationToken cancellationToken)
    {
        _products.TryGetValue(productId, out CatalogProduct? product);

        return Task.FromResult(product);
    }
}
