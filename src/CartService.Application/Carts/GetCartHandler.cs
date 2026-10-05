namespace CartService.Application.Carts;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;

public sealed class GetCartHandler
{
    private readonly ICartRepository _carts;
    private readonly ICartCache _cache;

    public GetCartHandler(ICartRepository carts, ICartCache cache)
    {
        _carts = carts;
        _cache = cache;
    }

    public async Task<CartDto> HandleAsync(Guid cartId, Requester requester, CancellationToken cancellationToken)
    {
        CachedCart? cached = await _cache.GetByIdAsync(cartId, cancellationToken);

        CartTelemetry.CacheLookups.Add(1, CartTelemetry.Tag("result", cached is null ? "miss" : "hit"));

        if (cached is not null)
        {
            // A cache hit must not skip the ownership check, and it gives the same answer as the database does.
            return cached.IsOwnedBy(requester) ? cached.Cart : throw new CartNotFoundException();
        }

        Cart cart = await _carts.GetOwnedAsync(cartId, requester, cancellationToken);
        CartDto dto = CartDto.From(cart);
        await _cache.SetAsync(cart, dto, cancellationToken);

        return dto;
    }
}
