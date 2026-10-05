namespace CartService.Application.Carts;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;

/// <summary>
/// Moves the items of a guest cart into the cart of the customer who just signed in. The guest proves access to
/// the guest cart with its secret token. The customer gets their active cart, or a new one if they have none.
/// </summary>
public sealed class MergeGuestCartHandler
{
    private readonly ICartRepository _carts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;
    private readonly ICartCache _cache;

    public MergeGuestCartHandler(ICartRepository carts, IUnitOfWork unitOfWork, TimeProvider time, ICartCache cache)
    {
        _carts = carts;
        _unitOfWork = unitOfWork;
        _time = time;
        _cache = cache;
    }

    /// <summary>
    /// Returns the cart of the customer after the merge. Merging a guest cart that was already merged changes
    /// nothing and returns the customer's current cart, so a client can safely repeat the request when it did not
    /// get the answer.
    /// </summary>
    /// <exception cref="CartNotFoundException">No guest cart has this token.</exception>
    public async Task<CartDto> HandleAsync(Guid customerId, string guestTokenHash, CancellationToken cancellationToken)
    {
        Cart guestCart = await _carts.GetByGuestTokenHashAsync(guestTokenHash, cancellationToken)
            ?? throw new CartNotFoundException();

        Cart? customerCart = await _carts.GetActiveByCustomerAsync(customerId, cancellationToken);

        if (guestCart.Status == CartStatus.Merged)
        {
            return CartDto.From(customerCart ?? throw new CartNotFoundException());
        }

        DateTimeOffset now = _time.GetUtcNow();

        if (customerCart is null)
        {
            customerCart = Cart.CreateForCustomer(customerId, now);
            _carts.Add(customerCart);
        }

        customerCart.MergeGuestCart(guestCart, now);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Both carts changed: the guest cart is now read-only and the customer cart has the items.
        await _cache.RemoveAsync(guestCart);
        await _cache.RemoveAsync(customerCart);
        CartTelemetry.Merges.Add(1);

        return CartDto.From(customerCart);
    }
}
