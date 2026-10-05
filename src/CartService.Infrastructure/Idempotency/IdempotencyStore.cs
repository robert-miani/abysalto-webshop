namespace CartService.Infrastructure.Idempotency;

using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

/// <summary>
/// Stores idempotency records in PostgreSQL. A key is claimed with one <c>INSERT ... ON CONFLICT DO NOTHING</c>:
/// the database decides which of several concurrent requests with the same key wins, so no application lock is
/// needed and it works with several service instances.
/// </summary>
internal sealed class IdempotencyStore : IIdempotencyStore
{
    private const int MaxClaimAttempts = 3;

    private readonly CartDbContext _context;
    private readonly IOptions<IdempotencyOptions> _options;

    public IdempotencyStore(CartDbContext context, IOptions<IdempotencyOptions> options)
    {
        _context = context;
        _options = options;
    }

    public async Task<IdempotencyClaim> BeginAsync(string scope, string key, string fingerprint, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaxClaimAttempts; attempt++)
        {
            await DeleteExpiredAsync(scope, key, cancellationToken);

            if (await TryInsertAsync(scope, key, fingerprint, cancellationToken))
            {
                return new IdempotencyClaim { Outcome = IdempotencyOutcome.Started };
            }

            IdempotencyClaim? existing = await ReadAsync(scope, key, fingerprint, cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            // The record disappeared between the insert and the read: it expired or its request gave the key
            // back. Try to claim it again.
        }

        throw new InvalidOperationException("The idempotency key could not be claimed because it kept changing.");
    }

    public async Task CompleteAsync(string scope, string key, int responseStatusCode, string? responseBody, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE idempotency_records
            SET status = 'Completed', response_status_code = @statusCode, response_body = @body, expires_at = now() + @retention
            WHERE scope = @scope AND "key" = @key AND status = 'InProgress'
            """;

        await using NpgsqlCommand command = await _context.CreateCommandAsync(sql, cancellationToken);
        command.Parameters.AddWithValue("scope", scope);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("statusCode", responseStatusCode);
        command.Parameters.Add(new NpgsqlParameter("body", NpgsqlDbType.Text) { Value = (object?)responseBody ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("retention", NpgsqlDbType.Interval) { Value = _options.Value.Retention });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReleaseAsync(string scope, string key, CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM idempotency_records
            WHERE scope = @scope AND "key" = @key AND status = 'InProgress'
            """;

        await using NpgsqlCommand command = await _context.CreateCommandAsync(sql, cancellationToken);
        command.Parameters.AddWithValue("scope", scope);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task DeleteExpiredAsync(string scope, string key, CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM idempotency_records
            WHERE scope = @scope AND "key" = @key AND expires_at < now()
            """;

        await using NpgsqlCommand command = await _context.CreateCommandAsync(sql, cancellationToken);
        command.Parameters.AddWithValue("scope", scope);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> TryInsertAsync(string scope, string key, string fingerprint, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO idempotency_records (scope, "key", fingerprint, status, created_at, expires_at)
            VALUES (@scope, @key, @fingerprint, 'InProgress', now(), now() + @lease)
            ON CONFLICT (scope, "key") DO NOTHING
            """;

        await using NpgsqlCommand command = await _context.CreateCommandAsync(sql, cancellationToken);
        command.Parameters.AddWithValue("scope", scope);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        command.Parameters.Add(new NpgsqlParameter("lease", NpgsqlDbType.Interval) { Value = _options.Value.InProgressLease });

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<IdempotencyClaim?> ReadAsync(string scope, string key, string fingerprint, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT fingerprint, status, response_status_code, response_body
            FROM idempotency_records
            WHERE scope = @scope AND "key" = @key
            """;

        await using NpgsqlCommand command = await _context.CreateCommandAsync(sql, cancellationToken);
        command.Parameters.AddWithValue("scope", scope);
        command.Parameters.AddWithValue("key", key);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        string storedFingerprint = reader.GetString(0);
        string status = reader.GetString(1);

        if (!string.Equals(storedFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return new IdempotencyClaim { Outcome = IdempotencyOutcome.KeyReused };
        }

        if (status == IdempotencyRecord.Completed)
        {
            return new IdempotencyClaim
            {
                Outcome = IdempotencyOutcome.Completed,
                ResponseStatusCode = reader.GetInt32(2),
                ResponseBody = reader.IsDBNull(3) ? null : reader.GetString(3),
            };
        }

        return new IdempotencyClaim { Outcome = IdempotencyOutcome.InProgress };
    }
}
