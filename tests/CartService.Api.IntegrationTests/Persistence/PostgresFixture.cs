namespace CartService.Api.IntegrationTests.Persistence;

using System.Threading.Tasks;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// One PostgreSQL container for all persistence tests. It starts once, applies the real migrations, and is
/// removed when the tests finish. Tests isolate themselves with new ids instead of cleaning the database.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Keep in sync with the image in docker-compose.yml.
    private const string Image = "postgres:17-alpine";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);

        await using CartDbContext context = CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    internal CartDbContext CreateContext()
    {
        return CreateContext(ConnectionString);
    }

    /// <summary>
    /// Creates a new, migrated database in the container and returns its connection string. Tests that look at
    /// every row of a table, such as the outbox relay tests, use their own database so that other tests cannot
    /// interfere.
    /// </summary>
    internal async Task<string> CreateIsolatedDatabaseAsync(string name)
    {
        await using NpgsqlConnection admin = new NpgsqlConnection(ConnectionString);
        await admin.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
        await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        string connectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
        await using CartDbContext context = CreateContext(connectionString);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        return connectionString;
    }

    internal static CartDbContext CreateContext(string connectionString)
    {
        DbContextOptionsBuilder<CartDbContext> options = new DbContextOptionsBuilder<CartDbContext>();
        options.UseCartDatabase(connectionString);

        return new CartDbContext(options.Options);
    }
}
