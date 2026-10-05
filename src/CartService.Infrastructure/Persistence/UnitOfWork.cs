namespace CartService.Infrastructure.Persistence;

using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal sealed class UnitOfWork : IUnitOfWork
{
    /// <summary>The name of the partial unique index in the migration. It must stay in sync.</summary>
    private const string ActiveCartIndex = "ux_carts_active_customer";

    private readonly CartDbContext _context;

    public UnitOfWork(CartDbContext context)
    {
        _context = context;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(exception);
        }
        catch (DbUpdateException exception) when (IsActiveCartViolation(exception))
        {
            throw new ActiveCartAlreadyExistsException(exception);
        }
    }

    private static bool IsActiveCartViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ActiveCartIndex,
        };
    }
}
