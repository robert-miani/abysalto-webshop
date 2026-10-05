namespace CartService.Application.Carts;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;

/// <summary>
/// Adds a product to a cart. The name and price come from the catalog, never from the client, so nobody can
/// set their own price.
/// </summary>
public sealed class AddItemHandler
{
    private readonly ICartRepository _carts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductCatalog _catalog;
    private readonly TimeProvider _time;
    private readonly ICartCache _cache;

    public AddItemHandler(ICartRepository carts, IUnitOfWork unitOfWork, IProductCatalog catalog, TimeProvider time, ICartCache cache)
    {
        _carts = carts;
        _unitOfWork = unitOfWork;
        _catalog = catalog;
        _time = time;
        _cache = cache;
    }

    public async Task<CartDto> HandleAsync(
        Guid cartId,
        Requester requester,
        string productId,
        int quantity,
        CancellationToken cancellationToken)
    {
        Cart cart = await _carts.GetOwnedAsync(cartId, requester, cancellationToken);

        CatalogProduct product = await _catalog.FindAsync(productId, cancellationToken)
            ?? throw new CartRuleViolationException(CartErrorCodes.ProductNotFound, $"Product '{productId}' does not exist.");

        cart.AddItem(product.ProductId, product.Name, product.UnitPrice, quantity, _time.GetUtcNow());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(cart, cancellationToken);

        return CartDto.From(cart);
    }
}
