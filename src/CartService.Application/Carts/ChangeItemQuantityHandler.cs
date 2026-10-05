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

    public ChangeItemQuantityHandler(ICartRepository carts, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _carts = carts;
        _unitOfWork = unitOfWork;
        _time = time;
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

        return CartDto.From(cart);
    }
}
