namespace CartService.Infrastructure.Health;

using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using CartService.Infrastructure.Outbox;
using CartService.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

/// <summary>
/// Tells whether the events of checkouts reach the Order service. The service cannot ask Service Bus directly:
/// it may only send, and the emulator has no management API. But if the relay cannot deliver, messages wait in
/// the outbox, so the age of the oldest waiting message shows that Service Bus or the relay has a problem. The
/// service stays ready, because carts still work and no event is lost; it is degraded, so somebody looks.
/// </summary>
internal sealed class OutboxHealthCheck : IHealthCheck
{
    private readonly CartDbContext _context;
    private readonly IOptions<OutboxOptions> _options;
    private readonly TimeProvider _time;

    public OutboxHealthCheck(CartDbContext context, IOptions<OutboxOptions> options, TimeProvider time)
    {
        _context = context;
        _options = options;
        _time = time;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        const string sql = """
            SELECT min(occurred_at) FILTER (WHERE processed_at IS NULL),
                   count(*) FILTER (WHERE processed_at IS NULL AND attempts >= @maxAttempts)
            FROM outbox_messages
            """;

        try
        {
            await using NpgsqlCommand command = await _context.CreateCommandAsync(sql, cancellationToken);
            command.Parameters.AddWithValue("maxAttempts", _options.Value.MaxAttempts);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);

            DateTimeOffset? oldestWaiting = reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0);
            long failed = reader.GetInt64(1);

            if (failed > 0)
            {
                return new HealthCheckResult(context.Registration.FailureStatus, $"{failed} event(s) could not be published and need attention.");
            }

            if (oldestWaiting is not null && _time.GetUtcNow() - oldestWaiting.Value > _options.Value.DegradedAfter)
            {
                return new HealthCheckResult(context.Registration.FailureStatus, "Events wait for longer than expected to be published.");
            }

            return HealthCheckResult.Healthy("Events are published in time.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Without the database nothing is ready anyway, and the database check says so.
            return new HealthCheckResult(context.Registration.FailureStatus, "The outbox could not be checked.", exception);
        }
    }
}
