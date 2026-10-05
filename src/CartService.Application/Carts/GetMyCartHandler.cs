namespace CartService.Application.Carts;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;

public sealed class GetMyCartHandler
{
    private readonly ICartRepository _carts;

    public GetMyCartHandler(ICartRepository carts)
    {
        _carts = carts;
    }

    /// <summary>
    /// Returns the customer's active cart, or raises <see cref="CartNotFoundException"/> when there is none.
    /// </summary>
    public async Task<CartDto> HandleAsync(Guid customerId, CancellationToken cancellationToken)
    {
        Cart cart = await _carts.GetActiveByCustomerAsync(customerId, cancellationToken)
            ?? throw new CartNotFoundException();

        return CartDto.From(cart);
    }
}
