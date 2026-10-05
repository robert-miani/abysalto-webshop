namespace CartService.Application.Abstractions;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Looks up products with their current name and price. In the platform this is the job of the Catalog and
/// Pricing services; the Cart service only depends on this port.
/// </summary>
public interface IProductCatalog
{
    /// <summary>
    /// Returns the product, or null when no product has this id.
    /// </summary>
    Task<CatalogProduct?> FindAsync(string productId, CancellationToken cancellationToken);
}
