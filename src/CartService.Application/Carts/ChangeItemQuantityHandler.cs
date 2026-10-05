namespace CartService.Application.Carts;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;

public sealed class ChangeItemQuantityHandler
{
    private readonly ICartRepository _carts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;
    private readonly ICartCache _cache;

    public ChangeItemQuantityHandler(ICartRepository carts, IUnitOfWork unitOfWork, TimeProvider time, ICartCache cache)
    {
        _carts = carts;
        _unitOfWork = unitOfWork;
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

        cart.ChangeQuantity(productId, quantity, _time.GetUtcNow());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(cart);

        return CartDto.From(cart);
    }
}
