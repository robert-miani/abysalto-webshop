namespace CartService.Infrastructure.Health;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

/// <summary>
/// The service cannot do anything without its database, so a failure here makes it not ready.
/// </summary>
internal sealed class DatabaseHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private readonly CartDbContext _context;

    public DatabaseHealthCheck(CartDbContext context)
    {
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await using NpgsqlCommand command = await _context.CreateCommandAsync("SELECT 1", timeout.Token);
            await command.ExecuteScalarAsync(timeout.Token);

            return HealthCheckResult.Healthy("The database answers.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The exception is kept for the logs of the health check service. The description is public and must
            // not reveal where the database is.
            return new HealthCheckResult(context.Registration.FailureStatus, "The database does not answer.", exception);
        }
    }
}
