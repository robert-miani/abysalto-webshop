namespace CartService.Api.IntegrationTests.Persistence;

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MigrationOnStartupTests
{
    private readonly PostgresFixture _postgres;

    public MigrationOnStartupTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartingTheServiceCreatesTheSchemaInAnEmptyDatabaseWhenEnabled()
    {
        string connectionString = await CreateEmptyDatabaseAsync("cart_migration_enabled");

        StartAndStopTheService(connectionString, applyMigrations: true);

        (await TableExistsAsync(connectionString, "carts")).ShouldBeTrue();
        (await TableExistsAsync(connectionString, "cart_items")).ShouldBeTrue();
    }

    [Fact]
    public async Task StartingTheServiceLeavesAnEmptyDatabaseUntouchedWhenDisabled()
    {
        string connectionString = await CreateEmptyDatabaseAsync("cart_migration_disabled");

        StartAndStopTheService(connectionString, applyMigrations: false);

        (await TableExistsAsync(connectionString, "carts")).ShouldBeFalse();
    }

    private static void StartAndStopTheService(string connectionString, bool applyMigrations)
    {
        using CartApiFactory factory = new CartApiFactory();
        using WebApplicationFactory<Program> configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:ConnectionString", connectionString);
            builder.UseSetting("Database:ApplyMigrationsOnStartup", applyMigrations ? "true" : "false");
        });

        // Creating a client builds and starts the host, and starting the host runs the hosted services.
        using HttpClient client = configured.CreateClient();
    }

    private async Task<string> CreateEmptyDatabaseAsync(string name)
    {
        await using NpgsqlConnection admin = new NpgsqlConnection(_postgres.ConnectionString);
        await admin.OpenAsync(Token);
        await using NpgsqlCommand create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
        await create.ExecuteNonQueryAsync(Token);

        return new NpgsqlConnectionStringBuilder(_postgres.ConnectionString) { Database = name }.ConnectionString;
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string table)
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using NpgsqlCommand command = new NpgsqlCommand("SELECT to_regclass(@name) IS NOT NULL", connection);
        command.Parameters.AddWithValue("name", $"public.{table}");

        return (bool)(await command.ExecuteScalarAsync(Token))!;
    }
}
