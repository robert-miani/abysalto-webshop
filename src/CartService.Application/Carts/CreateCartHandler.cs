namespace CartService.Application.Carts;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;

/// <summary>
/// Creates a cart. A customer gets their one active cart, a new one if they have none. Without a customer,
/// a guest cart is created together with the secret token that gives access to it.
/// </summary>
public sealed class CreateCartHandler
{
    private readonly ICartRepository _carts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IGuestTokenService _guestTokens;
    private readonly TimeProvider _time;

    public CreateCartHandler(
        ICartRepository carts,
        IUnitOfWork unitOfWork,
        IGuestTokenService guestTokens,
        TimeProvider time)
    {
        _carts = carts;
        _unitOfWork = unitOfWork;
        _guestTokens = guestTokens;
        _time = time;
    }

    public async Task<CreateCartResult> HandleAsync(Guid? customerId, CancellationToken cancellationToken)
    {
        if (customerId.HasValue)
        {
            return await CreateForCustomerAsync(customerId.Value, cancellationToken);
        }

        return await CreateForGuestAsync(cancellationToken);
    }

    private async Task<CreateCartResult> CreateForCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        Cart? existing = await _carts.GetActiveByCustomerAsync(customerId, cancellationToken);

        if (existing is not null)
        {
            return ExistingCart(existing);
        }

        Cart cart = Cart.CreateForCustomer(customerId, _time.GetUtcNow());
        _carts.Add(cart);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ActiveCartAlreadyExistsException)
        {
            // Another request created the cart at the same moment and won. Return its cart instead of failing.
            Cart winner = await _carts.GetActiveByCustomerAsync(customerId, cancellationToken)
                ?? throw new InvalidOperationException("The customer's active cart exists but could not be read.");

            return ExistingCart(winner);
        }

        return new CreateCartResult { Cart = CartDto.From(cart), Created = true };
    }

    private async Task<CreateCartResult> CreateForGuestAsync(CancellationToken cancellationToken)
    {
        GuestToken token = _guestTokens.Create();
        Cart cart = Cart.CreateForGuest(token.Hash, _time.GetUtcNow());
        _carts.Add(cart);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateCartResult { Cart = CartDto.From(cart), Created = true, GuestToken = token.Value };
    }

    private static CreateCartResult ExistingCart(Cart cart)
    {
        return new CreateCartResult { Cart = CartDto.From(cart), Created = false };
    }
}
