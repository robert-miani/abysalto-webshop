namespace CartService.Api.IntegrationTests.Caching;

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Infrastructure;
using CartService.Api.IntegrationTests.Persistence;
using CartService.Application.Carts;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

/// <summary>
/// The cache is an optimisation, so the service must work when Redis is gone and use it again when it is back.
/// These tests start and stop a Redis container of their own.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RedisOutageTests
{
    private readonly PostgresFixture _postgres;

    public RedisOutageTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithRedisThatNeverExistedEveryEndpointStillWorks()
    {
        using TestApi api = CreateApi("localhost:1");

        await UseAllCartEndpointsAsync(api);
    }

    [Fact]
    public async Task WhenRedisGoesAwayTheServiceKeepsWorkingAndUsesRedisAgainWhenItIsBack()
    {
        int port = FreeTcpPort();

        // Without persistence Redis comes back empty. A Redis that restores an old snapshot could return a cart
        // from before the outage; that stale copy lives at most as long as the entry lifetime.
        await using RedisContainer container = new RedisBuilder("redis:7-alpine")
            .WithPortBinding(port, 6379)
            .WithCommand("redis-server", "--save", string.Empty, "--appendonly", "no")
            .Build();
        await container.StartAsync(Token);
        using TestApi api = CreateApi($"localhost:{port}", breakDuration: "00:00:01");
        Guid customerId = Guid.NewGuid();
        using HttpClient client = api.Customer(customerId);
        CartDto cart = (await (await client.PostAsync("/v1/carts", content: null, Token)).Content.ReadFromJsonAsync<CartDto>(TestJson.Options, Token))!;
        await client.GetAsync($"/v1/carts/{cart.Id}", Token);
        string key = $"cart:v1:id:{cart.Id:N}";
        (await KeyExistsAsync(port, key)).ShouldBeTrue();

        await container.StopAsync(Token);

        // Redis is gone. Reads and changes still work, and none of them waits for the timeout once the
        // circuit is open.
        for (int attempt = 0; attempt < 12; attempt++)
        {
            (await client.GetAsync($"/v1/carts/{cart.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "cap-red", quantity = 1 }, Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        Stopwatch watch = Stopwatch.StartNew();
        (await client.GetAsync($"/v1/carts/{cart.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));

        // Redis comes back empty, so the change made during the outage is read from the database.
        await container.StartAsync(Token);
        await Task.Delay(TimeSpan.FromSeconds(2), Token);
        CartDto current = (await (await client.GetAsync($"/v1/carts/{cart.Id}", Token)).Content.ReadFromJsonAsync<CartDto>(TestJson.Options, Token))!;
        current.Items.ShouldHaveSingleItem().ProductId.ShouldBe("cap-red");

        await EventuallyAsync(async () =>
        {
            await client.GetAsync($"/v1/carts/{cart.Id}", Token);

            return await KeyExistsAsync(port, key);
        });
    }

    private static async Task UseAllCartEndpointsAsync(TestApi api)
    {
        Guid customerId = Guid.NewGuid();
        using HttpClient client = api.Customer(customerId);

        HttpResponseMessage created = await client.PostAsync("/v1/carts", content: null, Token);
        CartDto cart = (await created.Content.ReadFromJsonAsync<CartDto>(TestJson.Options, Token))!;
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "cap-red", quantity = 2 }, Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/v1/carts/{cart.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/v1/carts/me", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/v1/carts/{cart.Id}/items/cap-red", new { quantity = 3 }, Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync($"/v1/carts/{cart.Id}/checkout", content: null, Token)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    private TestApi CreateApi(string redis, string breakDuration = "00:00:15")
    {
        return new TestApi(_postgres, configure: builder =>
        {
            builder.UseSetting("Cache:ConnectionString", redis);
            builder.UseSetting("Cache:Timeout", "00:00:00.300");
            builder.UseSetting("Cache:BreakDuration", breakDuration);
        });
    }

    private static int FreeTcpPort()
    {
        using TcpListener listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<bool> KeyExistsAsync(int port, string key)
    {
        await using ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync($"localhost:{port}");

        return await connection.GetDatabase().KeyExistsAsync(key);
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);

        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition did not become true in time.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), Token);
        }
    }
}
