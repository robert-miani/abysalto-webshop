namespace CartService.Application.Carts;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;

public sealed class GetMyCartHandler
{
    private readonly ICartRepository _carts;
    private readonly ICartCache _cache;

    public GetMyCartHandler(ICartRepository carts, ICartCache cache)
    {
        _carts = carts;
        _cache = cache;
    }

    /// <summary>
    /// Returns the customer's active cart, or raises <see cref="CartNotFoundException"/> when there is none.
    /// </summary>
    public async Task<CartDto> HandleAsync(Guid customerId, CancellationToken cancellationToken)
    {
        CachedCart? cached = await _cache.GetActiveByCustomerAsync(customerId, cancellationToken);

        CartTelemetry.CacheLookups.Add(1, CartTelemetry.Tag("result", cached is null ? "miss" : "hit"));

        if (cached is not null)
        {
            return cached.Cart;
        }

        Cart cart = await _carts.GetActiveByCustomerAsync(customerId, cancellationToken)
            ?? throw new CartNotFoundException();
        CartDto dto = CartDto.From(cart);
        await _cache.SetAsync(cart, dto, cancellationToken);

        return dto;
    }
}
