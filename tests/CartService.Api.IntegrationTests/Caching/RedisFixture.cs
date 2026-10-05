namespace CartService.Api.IntegrationTests.Caching;

using System.Threading.Tasks;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

/// <summary>
/// One Redis container for the cache tests. It starts once and is removed when the tests finish. Tests isolate
/// themselves with new ids instead of cleaning Redis.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    // Keep in sync with the image in docker-compose.yml.
    private const string Image = "redis:7-alpine";

    private readonly RedisContainer _container = new RedisBuilder(Image).Build();
    private ConnectionMultiplexer? _connection;

    /// <summary>The address in the form the service expects: <c>host:port</c>.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>A direct connection, so tests can look at what is really stored.</summary>
    public IDatabase Database => _connection!.GetDatabase();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
        _connection = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
