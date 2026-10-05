namespace CartService.Infrastructure.Cleanup;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Npgsql;

/// <summary>
/// Deletes what is no longer needed. Every statement deletes one batch and the store repeats it until a batch is
/// not full, so a large backlog never holds a long lock. Deleting is idempotent, so two instances that clean at
/// the same time only do the work twice.
/// </summary>
internal sealed class CleanupStore
{
    private readonly CartDbContext _context;
    private readonly IOptions<CleanupOptions> _options;

    public CleanupStore(CartDbContext context, IOptions<CleanupOptions> options)
    {
        _context = context;
        _options = options;
    }

    public async Task<CleanupResult> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        CleanupOptions options = _options.Value;

        // The items of a deleted cart go with it (the foreign key cascades).
        int carts = await DeleteInBatchesAsync(
            """
            DELETE FROM carts
            WHERE id IN (
                SELECT id FROM carts
                WHERE (guest_token_hash IS NOT NULL AND status = 'Active' AND updated_at < @idleBefore)
                   OR (status = 'Merged' AND updated_at < @mergedBefore)
                ORDER BY updated_at
                LIMIT @batch)
            """,
            command =>
            {
                command.Parameters.AddWithValue("idleBefore", now - options.GuestCartIdleAfter);
                command.Parameters.AddWithValue("mergedBefore", now - options.MergedCartRetention);
            },
            cancellationToken);

        int outboxMessages = await DeleteInBatchesAsync(
            """
            DELETE FROM outbox_messages
            WHERE id IN (
                SELECT id FROM outbox_messages
                WHERE processed_at IS NOT NULL AND processed_at < @processedBefore
                ORDER BY processed_at
                LIMIT @batch)
            """,
            command => command.Parameters.AddWithValue("processedBefore", now - options.ProcessedOutboxRetention),
            cancellationToken);

        int idempotencyRecords = await DeleteInBatchesAsync(
            """
            DELETE FROM idempotency_records
            WHERE (scope, "key") IN (
                SELECT scope, "key" FROM idempotency_records
                WHERE expires_at < @now
                ORDER BY expires_at
                LIMIT @batch)
            """,
            command => command.Parameters.AddWithValue("now", now),
            cancellationToken);

        return new CleanupResult { Carts = carts, OutboxMessages = outboxMessages, IdempotencyRecords = idempotencyRecords };
    }

    private async Task<int> DeleteInBatchesAsync(string sql, Action<NpgsqlCommand> addParameters, CancellationToken cancellationToken)
    {
        int batchSize = _options.Value.BatchSize;
        int total = 0;
        int deleted;

        do
        {
            await using NpgsqlCommand command = await _context.CreateCommandAsync(sql, cancellationToken);
            command.Parameters.AddWithValue("batch", batchSize);
            addParameters(command);
            deleted = await command.ExecuteNonQueryAsync(cancellationToken);
            total += deleted;
        }
        while (deleted == batchSize);

        return total;
    }
}
