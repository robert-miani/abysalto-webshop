namespace CartService.Application.Carts;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;

internal static class CartRepositoryExtensions
{
    /// <summary>
    /// Loads a cart for a requester. A cart that does not exist and a cart of somebody else both raise
    /// <see cref="CartNotFoundException"/>, so the answer never reveals which cart ids exist.
    /// </summary>
    public static async Task<Cart> GetOwnedAsync(
        this ICartRepository repository,
        Guid cartId,
        Requester requester,
        CancellationToken cancellationToken)
    {
        Cart? cart = await repository.GetByIdAsync(cartId, cancellationToken);

        if (cart is null || !requester.Owns(cart))
        {
            throw new CartNotFoundException();
        }

        return cart;
    }
}
