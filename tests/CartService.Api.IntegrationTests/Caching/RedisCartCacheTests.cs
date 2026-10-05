namespace CartService.Api.IntegrationTests.Caching;

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using CartService.Domain;
using CartService.Infrastructure.Caching;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;
using Xunit;

[Collection(RedisCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RedisCartCacheTests : IDisposable
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    private readonly RedisFixture _redis;
    private readonly RedisCache _distributedCache;
    private readonly RedisCartCache _cache;

    public RedisCartCacheTests(RedisFixture redis)
    {
        _redis = redis;
        _distributedCache = CreateDistributedCache(redis.ConnectionString, TimeSpan.FromMilliseconds(300));
        _cache = new RedisCartCache(_distributedCache, Options.Create(new CartCacheOptions()), NullLogger<RedisCartCache>.Instance);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _distributedCache.Dispose();
    }

    [Fact]
    public async Task ACartComesBackAsItWasStored()
    {
        Cart cart = CustomerCartWithItems();
        CartDto dto = CartDto.From(cart);

        await _cache.SetAsync(cart, dto, Token);
        CachedCart? cached = await _cache.GetByIdAsync(cart.Id, Token);

        cached.ShouldNotBeNull();
        cached.Cart.Id.ShouldBe(cart.Id);
        cached.Cart.Status.ShouldBe(CartStatus.Active);
        cached.Cart.Total.ShouldBe(dto.Total);
        cached.Cart.Currency.ShouldBe("EUR");
        cached.Cart.CreatedAt.ShouldBe(dto.CreatedAt);
        cached.Cart.UpdatedAt.ShouldBe(dto.UpdatedAt);
        cached.Cart.Items.Select(item => (item.ProductId, item.Quantity, item.UnitPrice, item.LineTotal))
            .ShouldBe(dto.Items.Select(item => (item.ProductId, item.Quantity, item.UnitPrice, item.LineTotal)));
        cached.CustomerId.ShouldBe(cart.CustomerId);
        cached.GuestTokenHash.ShouldBeNull();
    }

    [Fact]
    public async Task AnUnknownCartIsAMiss()
    {
        (await _cache.GetByIdAsync(Guid.NewGuid(), Token)).ShouldBeNull();
        (await _cache.GetActiveByCustomerAsync(Guid.NewGuid(), Token)).ShouldBeNull();
    }

    [Fact]
    public async Task TheActiveCartOfACustomerIsFoundByTheCustomer()
    {
        Cart cart = CustomerCartWithItems();

        await _cache.SetAsync(cart, CartDto.From(cart), Token);
        CachedCart? cached = await _cache.GetActiveByCustomerAsync(cart.CustomerId!.Value, Token);

        cached.ShouldNotBeNull().Cart.Id.ShouldBe(cart.Id);
    }

    [Fact]
    public async Task ACartThatIsNotActiveIsNotFoundByTheCustomer()
    {
        Cart cart = CustomerCartWithItems();
        cart.Checkout(Now);

        await _cache.SetAsync(cart, CartDto.From(cart), Token);

        (await _cache.GetByIdAsync(cart.Id, Token)).ShouldNotBeNull().Cart.Status.ShouldBe(CartStatus.CheckoutPending);
        (await _cache.GetActiveByCustomerAsync(cart.CustomerId!.Value, Token)).ShouldBeNull();
    }

    [Fact]
    public async Task AGuestCartKeepsOnlyTheHashOfItsToken()
    {
        Cart cart = Cart.CreateForGuest("hash-of-the-token", Now);

        await _cache.SetAsync(cart, CartDto.From(cart), Token);
        CachedCart? cached = await _cache.GetByIdAsync(cart.Id, Token);

        cached.ShouldNotBeNull().GuestTokenHash.ShouldBe("hash-of-the-token");
        cached.CustomerId.ShouldBeNull();
        cached.IsOwnedBy(Requester.ForGuest("hash-of-the-token")).ShouldBeTrue();
    }

    [Fact]
    public async Task RemovingACartRemovesItUnderBothKeys()
    {
        Cart cart = CustomerCartWithItems();
        await _cache.SetAsync(cart, CartDto.From(cart), Token);

        await _cache.RemoveAsync(cart, Token);

        (await _cache.GetByIdAsync(cart.Id, Token)).ShouldBeNull();
        (await _cache.GetActiveByCustomerAsync(cart.CustomerId!.Value, Token)).ShouldBeNull();
    }

    [Fact]
    public async Task AnEntryExpiresAfterTheConfiguredLifetime()
    {
        Cart cart = CustomerCartWithItems();
        await _cache.SetAsync(cart, CartDto.From(cart), Token);

        RedisKey key = FindKey($"*id:{cart.Id:N}");

        TimeSpan? timeToLive = await _redis.Database.KeyTimeToLiveAsync(key);
        timeToLive.ShouldNotBeNull().ShouldBeInRange(TimeSpan.FromSeconds(55), TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task AnEntryThatCannotBeReadIsAMiss()
    {
        Cart cart = CustomerCartWithItems();
        await _cache.SetAsync(cart, CartDto.From(cart), Token);
        RedisKey key = FindKey($"*id:{cart.Id:N}");

        // The Redis cache keeps a value in a hash with the fields data, absexp and sldexp.
        await _redis.Database.HashSetAsync(key, "data", "this is not json");

        (await _cache.GetByIdAsync(cart.Id, Token)).ShouldBeNull();
    }

    [Fact]
    public async Task PingSucceedsWhenRedisAnswers()
    {
        await Should.NotThrowAsync(() => _cache.PingAsync(Token));
    }

    [Fact]
    public async Task ACancelledCallIsNotSwallowed()
    {
        using CancellationTokenSource cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => _cache.GetByIdAsync(Guid.NewGuid(), cancelled.Token));
    }

    [Fact]
    public async Task WhenRedisIsUnreachableTheCacheJustHasNothing()
    {
        using RedisCache unreachable = CreateDistributedCache("localhost:1", TimeSpan.FromMilliseconds(200));
        RedisCartCache cache = CreateCache(unreachable);
        Cart cart = CustomerCartWithItems();

        await Should.NotThrowAsync(async () =>
        {
            await cache.SetAsync(cart, CartDto.From(cart), Token);
            await cache.RemoveAsync(cart, Token);
        });
        (await cache.GetByIdAsync(cart.Id, Token)).ShouldBeNull();
        (await cache.GetActiveByCustomerAsync(cart.CustomerId!.Value, Token)).ShouldBeNull();
        await Should.ThrowAsync<Exception>(() => cache.PingAsync(Token));
    }

    [Fact]
    public async Task AfterRepeatedFailuresTheCacheIsSkippedWithoutWaiting()
    {
        using RedisCache unreachable = CreateDistributedCache("localhost:1", TimeSpan.FromMilliseconds(200));
        RedisCartCache cache = CreateCache(unreachable);

        // Enough failed calls to open the circuit.
        for (int attempt = 0; attempt < 10; attempt++)
        {
            await cache.GetByIdAsync(Guid.NewGuid(), Token);
        }

        Stopwatch watch = Stopwatch.StartNew();

        for (int attempt = 0; attempt < 200; attempt++)
        {
            await cache.GetByIdAsync(Guid.NewGuid(), Token);
        }

        watch.Stop();

        // 200 calls that each waited for the timeout would take 40 seconds.
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    private static RedisCartCache CreateCache(RedisCache distributedCache)
    {
        CartCacheOptions options = new CartCacheOptions
        {
            Timeout = TimeSpan.FromMilliseconds(200),
            BreakDuration = TimeSpan.FromSeconds(30),
        };

        return new RedisCartCache(distributedCache, Options.Create(options), NullLogger<RedisCartCache>.Instance);
    }

    private static RedisCache CreateDistributedCache(string connectionString, TimeSpan timeout)
    {
        ConfigurationOptions configuration = ConfigurationOptions.Parse(connectionString);
        configuration.AbortOnConnectFail = false;
        configuration.ConnectTimeout = (int)timeout.TotalMilliseconds;
        configuration.SyncTimeout = (int)timeout.TotalMilliseconds;
        configuration.AsyncTimeout = (int)timeout.TotalMilliseconds;

        return new RedisCache(Options.Create(new RedisCacheOptions { ConfigurationOptions = configuration }));
    }

    private static Cart CustomerCartWithItems()
    {
        Cart cart = Cart.CreateForCustomer(Guid.NewGuid(), Now);
        cart.AddItem("tee-blue-m", "Blue T-shirt M", Money.Eur(19.90m), 2, Now);
        cart.AddItem("cap-red", "Red cap", Money.Eur(9.50m), 1, Now);

        return cart;
    }

    private RedisKey FindKey(string pattern)
    {
        return _redis.Database.Multiplexer.GetServers().SelectMany(server => server.Keys(pattern: pattern)).Single();
    }
}
