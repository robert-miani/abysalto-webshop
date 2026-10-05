namespace CartService.Infrastructure.Caching;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using CartService.Domain;

/// <summary>
/// A cache that stores nothing. The service uses it when no cache is configured, and then reads every cart from
/// the database, exactly as it does when the real cache is down.
/// </summary>
internal sealed class NullCartCache : ICartCache
{
    public Task<CachedCart?> GetByIdAsync(Guid cartId, CancellationToken cancellationToken)
    {
        return Task.FromResult<CachedCart?>(null);
    }

    public Task<CachedCart?> GetActiveByCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return Task.FromResult<CachedCart?>(null);
    }

    public Task SetAsync(Cart cart, CartDto dto, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Cart cart)
    {
        return Task.CompletedTask;
    }
}
