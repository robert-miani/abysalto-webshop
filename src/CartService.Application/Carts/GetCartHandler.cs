namespace CartService.Application.Carts;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;

public sealed class GetCartHandler
{
    private readonly ICartRepository _carts;

    public GetCartHandler(ICartRepository carts)
    {
        _carts = carts;
    }

    public async Task<CartDto> HandleAsync(Guid cartId, Requester requester, CancellationToken cancellationToken)
    {
        Cart cart = await _carts.GetOwnedAsync(cartId, requester, cancellationToken);

        return CartDto.From(cart);
    }
}
