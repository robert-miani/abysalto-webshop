namespace CartService.Application.Abstractions;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Carts;
using CartService.Domain;

/// <summary>
/// A copy of carts that were read recently, so a read does not always have to go to the database. The database is
/// the only source of truth: the cache may be empty, slow, or down at any time, and the service must work without
/// it. For that reason no method throws: a failure of the cache is the same as a cache that has nothing.
/// </summary>
public interface ICartCache
{
    /// <summary>Returns the cached cart, or null when it is not cached or the cache cannot answer.</summary>
    Task<CachedCart?> GetByIdAsync(Guid cartId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the cached active cart of the customer, or null when it is not cached or the cache cannot answer.
    /// </summary>
    Task<CachedCart?> GetActiveByCustomerAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>Remembers a cart that was just read from the database.</summary>
    Task SetAsync(Cart cart, CartDto dto, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets everything cached about a cart. Every change of a cart calls this after it was saved, so the next
    /// read loads the new state.
    /// </summary>
    Task RemoveAsync(Cart cart, CancellationToken cancellationToken);
}
