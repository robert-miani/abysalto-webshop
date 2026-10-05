namespace CartService.Application.Abstractions;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Domain;

/// <summary>
/// Loads and stores carts. Changes are only written by <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface ICartRepository
{
    Task<Cart?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the customer's active cart, or null when the customer has none.
    /// </summary>
    Task<Cart?> GetActiveByCustomerAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the guest cart whose secret token has this hash, or null when there is none.
    /// </summary>
    Task<Cart?> GetByGuestTokenHashAsync(string guestTokenHash, CancellationToken cancellationToken);

    void Add(Cart cart);
}
