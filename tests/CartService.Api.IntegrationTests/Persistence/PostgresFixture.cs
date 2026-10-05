namespace CartService.Api.IntegrationTests.Persistence;

using System.Threading.Tasks;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
        DbContextOptionsBuilder<CartDbContext> options = new DbContextOptionsBuilder<CartDbContext>();
        options.UseCartDatabase(ConnectionString);

        return new CartDbContext(options.Options);
    }
}
