namespace CartService.Application.Tests.Fakes;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;

/// <summary>
/// Behaves like the real repository: a cart added to it only becomes visible when the unit of work saves.
/// </summary>
internal sealed class InMemoryCartRepository : ICartRepository
{
    private readonly List<Cart> _saved = new List<Cart>();
    private readonly List<Cart> _pending = new List<Cart>();

    public IReadOnlyList<Cart> SavedCarts => _saved;

    public Task<Cart?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return Task.FromResult(_saved.FirstOrDefault(cart => cart.Id == id));
    }

    public Task<Cart?> GetActiveByCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        Cart? cart = _saved.FirstOrDefault(candidate => candidate.CustomerId == customerId && candidate.Status == CartStatus.Active);

        return Task.FromResult(cart);
    }

    public void Add(Cart cart)
    {
        _pending.Add(cart);
    }

    /// <summary>Puts a cart into the store as if an earlier request had saved it.</summary>
    public void Seed(Cart cart)
    {
        _saved.Add(cart);
    }

    internal void Commit()
    {
        _saved.AddRange(_pending);
        _pending.Clear();
    }

    internal void Discard()
    {
        _pending.Clear();
    }
}
