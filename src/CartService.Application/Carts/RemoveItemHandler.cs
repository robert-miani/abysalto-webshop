namespace CartService.Application.Carts;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;

public sealed class RemoveItemHandler
{
    private readonly ICartRepository _carts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public RemoveItemHandler(ICartRepository carts, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _carts = carts;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    /// <summary>
    /// Removes a product from the cart. Removing a product that is not in the cart is not an error, so a
    /// repeated request is safe.
    /// </summary>
    public async Task HandleAsync(Guid cartId, Requester requester, string productId, CancellationToken cancellationToken)
    {
        Cart cart = await _carts.GetOwnedAsync(cartId, requester, cancellationToken);

        cart.RemoveItem(productId, _time.GetUtcNow());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
