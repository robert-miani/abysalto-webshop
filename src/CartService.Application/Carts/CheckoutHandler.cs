namespace CartService.Application.Carts;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Application.Events;
using CartService.Domain;

/// <summary>
/// Starts checkout. The cart becomes read-only and a <see cref="CartCheckedOut"/> event is stored in the same
/// transaction, so the Order service hears about every checkout that happened and about no checkout that did not.
/// </summary>
public sealed class CheckoutHandler
{
    private readonly ICartRepository _carts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOutbox _outbox;
    private readonly TimeProvider _time;
    private readonly ICartCache _cache;

    public CheckoutHandler(ICartRepository carts, IUnitOfWork unitOfWork, IOutbox outbox, TimeProvider time, ICartCache cache)
    {
        _carts = carts;
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _time = time;
        _cache = cache;
    }

    public async Task<CheckoutResult> HandleAsync(Guid cartId, Requester requester, CancellationToken cancellationToken)
    {
        Cart cart = await _carts.GetOwnedAsync(cartId, requester, cancellationToken);

        if (cart.Status == CartStatus.CheckoutPending && cart.CheckoutId.HasValue)
        {
            // The checkout was already started, for example because the client repeated its request after a
            // timeout. Answer with the same checkout and do not publish a second event.
            return new CheckoutResult { CheckoutId = cart.CheckoutId.Value };
        }

        DateTimeOffset now = _time.GetUtcNow();
        Guid checkoutId = cart.Checkout(now);

        _outbox.Add(new CartCheckedOut
        {
            EventId = Guid.CreateVersion7(),
            OccurredAt = now,
            CheckoutId = checkoutId,
            CartId = cart.Id,
            CustomerId = cart.CustomerId!.Value,
            Items = cart.Items
                .OrderBy(item => item.ProductId, StringComparer.Ordinal)
                .Select(item => new CartCheckedOutItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice.Amount,
                })
                .ToList(),
            IndicativeTotal = cart.Total.Amount,
            Currency = cart.Total.Currency,
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(cart);
        CartTelemetry.Checkouts.Add(1);

        return new CheckoutResult { CheckoutId = checkoutId };
    }
}
