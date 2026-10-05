namespace CartService.Api.IntegrationTests.Outbox;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;

/// <summary>
/// Wraps a repository and runs a hook right after a cart was loaded. A test uses it to let "another request"
/// change the cart between the moment the handler read it and the moment it saves.
/// </summary>
internal sealed class HookedCartRepository : ICartRepository
{
    private readonly ICartRepository _inner;
    private readonly Func<Task> _afterLoad;

    public HookedCartRepository(ICartRepository inner, Func<Task> afterLoad)
    {
        _inner = inner;
        _afterLoad = afterLoad;
    }

    public async Task<Cart?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        Cart? cart = await _inner.GetByIdAsync(id, cancellationToken);
        await _afterLoad();

        return cart;
    }

    public Task<Cart?> GetActiveByCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return _inner.GetActiveByCustomerAsync(customerId, cancellationToken);
    }

    public Task<Cart?> GetByGuestTokenHashAsync(string guestTokenHash, CancellationToken cancellationToken)
    {
        return _inner.GetByGuestTokenHashAsync(guestTokenHash, cancellationToken);
    }

    public void Add(Cart cart)
    {
        _inner.Add(cart);
    }
}
