namespace CartService.Infrastructure.Outbox;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

/// <summary>
/// The relay's view of the outbox table. A message is claimed with a lease instead of a long transaction: the
/// claim is one atomic statement, so the connection is never held open while the message is published to a
/// broker.
/// </summary>
internal sealed class OutboxStore
{
    private readonly CartDbContext _context;

    public OutboxStore(CartDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Claims up to <paramref name="batchSize"/> unprocessed messages, oldest first, and leases them to the caller.
    /// Messages that another relay has leased, and messages that failed <paramref name="maxAttempts"/> times, are
    /// skipped. Concurrent relays get different messages because of <c>FOR UPDATE SKIP LOCKED</c>.
    /// </summary>
    public async Task<IReadOnlyList<ClaimedMessage>> ClaimBatchAsync(
        int batchSize,
        TimeSpan lease,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE outbox_messages AS message
            SET locked_until = now() + @lease, attempts = message.attempts + 1
            WHERE message.id IN (
                SELECT candidate.id
                FROM outbox_messages AS candidate
                WHERE candidate.processed_at IS NULL
                  AND candidate.attempts < @maxAttempts
                  AND (candidate.locked_until IS NULL OR candidate.locked_until < now())
                ORDER BY candidate.occurred_at
                LIMIT @batchSize
                FOR UPDATE SKIP LOCKED)
            RETURNING message.id, message.type, message.ordering_key, message.payload::text, message.occurred_at, message.attempts
            """;

        await using NpgsqlCommand command = await CreateCommandAsync(sql, cancellationToken);
        command.Parameters.Add(new NpgsqlParameter("lease", NpgsqlDbType.Interval) { Value = lease });
        command.Parameters.AddWithValue("maxAttempts", maxAttempts);
        command.Parameters.AddWithValue("batchSize", batchSize);

        List<ClaimedMessage> claimed = new List<ClaimedMessage>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            claimed.Add(new ClaimedMessage
            {
                Id = reader.GetGuid(0),
                Type = reader.GetString(1),
                OrderingKey = reader.GetString(2),
                Payload = reader.GetString(3),
                OccurredAt = reader.GetFieldValue<DateTimeOffset>(4),
                Attempt = reader.GetInt32(5),
            });
        }

        claimed.Sort((left, right) => left.OccurredAt.CompareTo(right.OccurredAt));

        return claimed;
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE outbox_messages
            SET processed_at = now(), locked_until = NULL, last_error = NULL
            WHERE id = @id
            """;

        await using NpgsqlCommand command = await CreateCommandAsync(sql, cancellationToken);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Records a failed attempt and delays the next one: the message stays leased for <paramref name="retryDelay"/>.
    /// </summary>
    public async Task MarkFailedAsync(Guid id, string error, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE outbox_messages
            SET locked_until = now() + @delay, last_error = @error
            WHERE id = @id
            """;

        string shortError = error.Length <= OutboxMessageConfiguration.MaxErrorLength
            ? error
            : error[..OutboxMessageConfiguration.MaxErrorLength];

        await using NpgsqlCommand command = await CreateCommandAsync(sql, cancellationToken);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.Add(new NpgsqlParameter("delay", NpgsqlDbType.Interval) { Value = retryDelay });
        command.Parameters.AddWithValue("error", shortError);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlCommand> CreateCommandAsync(string sql, CancellationToken cancellationToken)
    {
        await _context.Database.OpenConnectionAsync(cancellationToken);
        NpgsqlConnection connection = (NpgsqlConnection)_context.Database.GetDbConnection();

        return new NpgsqlCommand(sql, connection);
    }
}
