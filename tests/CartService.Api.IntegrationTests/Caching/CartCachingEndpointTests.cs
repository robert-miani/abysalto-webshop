namespace CartService.Api.IntegrationTests.Caching;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Infrastructure;
using CartService.Api.IntegrationTests.Persistence;
using CartService.Application.Carts;
using CartService.Domain;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

[Collection(CacheCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CartCachingEndpointTests : IDisposable
{
    private readonly PostgresFixture _postgres;
    private readonly RedisFixture _redis;
    private readonly TestApi _api;

    public CartCachingEndpointTests(PostgresFixture postgres, RedisFixture redis)
    {
        _postgres = postgres;
        _redis = redis;
        _api = new TestApi(postgres, configure: builder => builder.UseSetting("Cache:ConnectionString", redis.ConnectionString));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _api.Dispose();
    }

    [Fact]
    public async Task TheFirstReadStoresTheCartAndTheSecondReadComesFromRedis()
    {
        (HttpClient client, CartDto cart, _) = await CreateCustomerCartAsync();
        (await IdKeyExistsAsync(cart.Id)).ShouldBeFalse();

        CartDto first = await GetAsync(client, cart.Id);
        (await IdKeyExistsAsync(cart.Id)).ShouldBeTrue();

        // Change the row behind the cache's back: only a read from Redis still shows the old time.
        DateTimeOffset later = first.UpdatedAt.AddHours(1);
        await SetUpdatedAtInDatabaseAsync(cart.Id, later);
        CartDto second = await GetAsync(client, cart.Id);

        second.UpdatedAt.ShouldBe(first.UpdatedAt);
    }

    [Fact]
    public async Task AChangeIsVisibleAtOnceBecauseItDropsTheCachedCart()
    {
        (HttpClient client, CartDto cart, _) = await CreateCustomerCartAsync();
        await GetAsync(client, cart.Id);

        await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "cap-red", quantity = 2 }, Token);

        (await IdKeyExistsAsync(cart.Id)).ShouldBeFalse();
        CartDto current = await GetAsync(client, cart.Id);
        current.Items.ShouldHaveSingleItem().Quantity.ShouldBe(2);
        (await IdKeyExistsAsync(cart.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task ACachedCartIsStillOnlyForItsOwner()
    {
        (HttpClient owner, CartDto cart, _) = await CreateCustomerCartAsync();
        await GetAsync(owner, cart.Id);
        (await IdKeyExistsAsync(cart.Id)).ShouldBeTrue();
        using HttpClient stranger = _api.Customer(Guid.NewGuid());
        using HttpClient anonymous = _api.Anonymous();

        (await stranger.GetAsync($"/v1/carts/{cart.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/v1/carts/{cart.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(owner, cart.Id)).Id.ShouldBe(cart.Id);
    }

    [Fact]
    public async Task ACachedGuestCartIsOnlyForTheHolderOfItsToken()
    {
        using HttpClient anonymous = _api.Anonymous();
        GuestCartCreatedResponseBody created = (await (await anonymous.PostAsync("/v1/carts", content: null, Token))
            .Content.ReadFromJsonAsync<GuestCartCreatedResponseBody>(TestJson.Options, Token))!;
        using HttpClient guest = _api.Guest(created.GuestToken);
        using HttpClient otherGuest = _api.Guest("another-token");

        await GetAsync(guest, created.Cart.Id);
        await GetAsync(guest, created.Cart.Id);

        (await IdKeyExistsAsync(created.Cart.Id)).ShouldBeTrue();
        (await otherGuest.GetAsync($"/v1/carts/{created.Cart.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MyCartIsCachedAndCheckoutDropsIt()
    {
        (HttpClient client, CartDto cart, Guid customerId) = await CreateCustomerCartAsync();
        await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "cap-red", quantity = 1 }, Token);

        CartDto mine = await ReadAsync<CartDto>(await client.GetAsync("/v1/carts/me", Token));
        (await _redis.Database.KeyExistsAsync($"cart:v1:customer:{customerId:N}")).ShouldBeTrue();
        mine.Id.ShouldBe(cart.Id);

        (await client.PostAsync($"/v1/carts/{cart.Id}/checkout", content: null, Token)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        (await _redis.Database.KeyExistsAsync($"cart:v1:customer:{customerId:N}")).ShouldBeFalse();
        (await client.GetAsync("/v1/carts/me", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(client, cart.Id)).Status.ShouldBe(CartStatus.CheckoutPending);
    }

    [Fact]
    public async Task MergingDropsTheGuestCartAndTheCustomerCart()
    {
        (HttpClient customer, CartDto customerCart, _) = await CreateCustomerCartAsync();
        using HttpClient anonymous = _api.Anonymous();
        GuestCartCreatedResponseBody created = (await (await anonymous.PostAsync("/v1/carts", content: null, Token))
            .Content.ReadFromJsonAsync<GuestCartCreatedResponseBody>(TestJson.Options, Token))!;
        using HttpClient guest = _api.Guest(created.GuestToken);
        await guest.PostAsJsonAsync($"/v1/carts/{created.Cart.Id}/items", new { productId = "mug-white", quantity = 1 }, Token);
        await GetAsync(guest, created.Cart.Id);
        await GetAsync(customer, customerCart.Id);

        using HttpRequestMessage merge = new HttpRequestMessage(HttpMethod.Post, "/v1/carts/me/merge");
        merge.Headers.Add("X-Cart-Token", created.GuestToken);
        HttpResponseMessage response = await customer.SendAsync(merge, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await IdKeyExistsAsync(created.Cart.Id)).ShouldBeFalse();
        (await IdKeyExistsAsync(customerCart.Id)).ShouldBeFalse();
        (await GetAsync(customer, customerCart.Id)).Items.ShouldHaveSingleItem().ProductId.ShouldBe("mug-white");
        (await GetAsync(guest, created.Cart.Id)).Status.ShouldBe(CartStatus.Merged);
    }

    [Fact]
    public async Task AnEntryThatCannotBeReadIsReplacedByTheDatabaseAnswer()
    {
        (HttpClient client, CartDto cart, _) = await CreateCustomerCartAsync();
        await GetAsync(client, cart.Id);
        await _redis.Database.HashSetAsync($"cart:v1:id:{cart.Id:N}", "data", "garbage");

        CartDto read = await GetAsync(client, cart.Id);

        read.Id.ShouldBe(cart.Id);
    }

    private async Task<(HttpClient Client, CartDto Cart, Guid CustomerId)> CreateCustomerCartAsync()
    {
        Guid customerId = Guid.NewGuid();
        HttpClient client = _api.Customer(customerId);
        CartDto cart = await ReadAsync<CartDto>(await client.PostAsync("/v1/carts", content: null, Token));

        return (client, cart, customerId);
    }

    private static async Task<CartDto> GetAsync(HttpClient client, Guid cartId)
    {
        HttpResponseMessage response = await client.GetAsync($"/v1/carts/{cartId}", Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ReadAsync<CartDto>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        T? body = await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Token);

        return body ?? throw new InvalidOperationException("The response body was empty.");
    }

    private async Task<bool> IdKeyExistsAsync(Guid cartId)
    {
        return await _redis.Database.KeyExistsAsync($"cart:v1:id:{cartId:N}");
    }

    private async Task SetUpdatedAtInDatabaseAsync(Guid cartId, DateTimeOffset updatedAt)
    {
        await using CartDbContext context = _postgres.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE carts SET updated_at = {updatedAt} WHERE id = {cartId}", Token);
    }

    private sealed class GuestCartCreatedResponseBody
    {
        public required string GuestToken { get; init; }

        public required CartDto Cart { get; init; }
    }
}
