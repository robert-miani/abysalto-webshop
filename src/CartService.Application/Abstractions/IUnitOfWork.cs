namespace CartService.Application.Abstractions;

using System.Threading;
using System.Threading.Tasks;

public interface IUnitOfWork
{
    /// <summary>
    /// Writes all pending changes in one transaction.
    /// </summary>
    /// <exception cref="ConcurrencyConflictException">Another request changed a cart in the meantime.</exception>
    /// <exception cref="ActiveCartAlreadyExistsException">The customer already has an active cart.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
