namespace CartService.Application.Tests.Fakes;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using CartService.Domain;

/// <summary>
/// A cache in memory that counts how it is used, so tests can tell a read from the cache from a read from the
/// database.
/// </summary>
internal sealed class FakeCartCache : ICartCache
{
    private readonly Dictionary<Guid, CachedCart> _byId = new Dictionary<Guid, CachedCart>();
    private readonly Dictionary<Guid, CachedCart> _byCustomer = new Dictionary<Guid, CachedCart>();

    public int Hits { get; private set; }

    public int Misses { get; private set; }

    public int Removals { get; private set; }

    public bool Contains(Guid cartId)
    {
        return _byId.ContainsKey(cartId);
    }

    public bool ContainsActiveCartOf(Guid customerId)
    {
        return _byCustomer.ContainsKey(customerId);
    }

    /// <summary>Puts an entry into the cache as if an earlier read had stored it.</summary>
    public void Seed(Cart cart, CartDto dto)
    {
        Store(cart, dto);
    }

    public Task<CachedCart?> GetByIdAsync(Guid cartId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Count(_byId.GetValueOrDefault(cartId)));
    }

    public Task<CachedCart?> GetActiveByCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Count(_byCustomer.GetValueOrDefault(customerId)));
    }

    public Task SetAsync(Cart cart, CartDto dto, CancellationToken cancellationToken)
    {
        Store(cart, dto);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(Cart cart, CancellationToken cancellationToken)
    {
        Removals++;
        _byId.Remove(cart.Id);

        if (cart.CustomerId.HasValue)
        {
            _byCustomer.Remove(cart.CustomerId.Value);
        }

        return Task.CompletedTask;
    }

    private void Store(Cart cart, CartDto dto)
    {
        CachedCart cached = CachedCart.From(cart, dto);
        _byId[cart.Id] = cached;

        if (cart.CustomerId.HasValue && cart.Status == CartStatus.Active)
        {
            _byCustomer[cart.CustomerId.Value] = cached;
        }
    }

    private CachedCart? Count(CachedCart? cached)
    {
        if (cached is null)
        {
            Misses++;
        }
        else
        {
            Hits++;
        }

        return cached;
    }
}
