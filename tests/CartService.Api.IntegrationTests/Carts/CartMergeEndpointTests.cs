namespace CartService.Api.IntegrationTests.Carts;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.Carts;
using CartService.Api.IntegrationTests.Infrastructure;
using CartService.Api.IntegrationTests.Persistence;
using CartService.Application.Carts;
using CartService.Domain;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CartMergeEndpointTests : IDisposable
{
    private const string MergeUrl = "/v1/carts/me/merge";

    private readonly TestApi _api;

    public CartMergeEndpointTests(PostgresFixture postgres)
    {
        _api = new TestApi(postgres);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _api.Dispose();
    }

    [Fact]
    public async Task AGuestWhoSignsInKeepsTheirItems()
    {
        GuestSession guest = await CreateGuestWithItemsAsync(("tee-blue-m", 2), ("cap-red", 1));
        using HttpClient customer = _api.Customer(Guid.NewGuid());

        HttpResponseMessage response = await MergeAsync(customer, guest.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        CartDto merged = await ReadAsync<CartDto>(response);
        merged.Items.Select(item => (item.ProductId, item.Quantity)).ToArray()
            .ShouldBe(new[] { ("cap-red", 1), ("tee-blue-m", 2) });
        merged.Total.ShouldBe((2 * 19.90m) + 9.50m);
        CartDto mine = await ReadAsync<CartDto>(await customer.GetAsync("/v1/carts/me", Token));
        mine.Id.ShouldBe(merged.Id);
        mine.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task TheCustomerCartAndTheGuestCartAreCombinedAndQuantitiesAreCapped()
    {
        Guid customerId = Guid.NewGuid();
        using HttpClient customer = _api.Customer(customerId);
        CartDto existing = await ReadAsync<CartDto>(await customer.PostAsync("/v1/carts", content: null, Token));
        await AddAsync(customer, existing.Id, "mug-white", 15);
        await AddAsync(customer, existing.Id, "socks-wool", 1);
        GuestSession guest = await CreateGuestWithItemsAsync(("mug-white", 12), ("cap-red", 1));

        CartDto merged = await ReadAsync<CartDto>(await MergeAsync(customer, guest.Token));

        merged.Id.ShouldBe(existing.Id);
        merged.Items.Single(item => item.ProductId == "mug-white").Quantity.ShouldBe(CartLimits.MaxQuantityPerItem);
        merged.Items.Single(item => item.ProductId == "cap-red").Quantity.ShouldBe(1);
        merged.Items.Single(item => item.ProductId == "socks-wool").Quantity.ShouldBe(1);
    }

    [Fact]
    public async Task MergingTwiceDoesNotDoubleTheQuantities()
    {
        GuestSession guest = await CreateGuestWithItemsAsync(("tee-blue-m", 3));
        using HttpClient customer = _api.Customer(Guid.NewGuid());
        await MergeAsync(customer, guest.Token);

        HttpResponseMessage again = await MergeAsync(customer, guest.Token);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        CartDto cart = await ReadAsync<CartDto>(again);
        cart.Items.ShouldHaveSingleItem().Quantity.ShouldBe(3);
    }

    [Fact]
    public async Task AfterTheMergeTheGuestCartIsReadOnly()
    {
        GuestSession guest = await CreateGuestWithItemsAsync(("tee-blue-m", 1));
        using HttpClient customer = _api.Customer(Guid.NewGuid());
        await MergeAsync(customer, guest.Token);

        CartDto guestCart = await ReadAsync<CartDto>(await guest.Client.GetAsync($"/v1/carts/{guest.CartId}", Token));
        HttpResponseMessage add = await AddAsync(guest.Client, guest.CartId, "cap-red", 1);

        guestCart.Status.ShouldBe(CartStatus.Merged);
        add.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        JsonElement problem = await add.Content.ReadFromJsonAsync<JsonElement>(Token);
        problem.GetProperty("code").GetString().ShouldBe("cart.not_active");
    }

    [Fact]
    public async Task AGuestCartCannotBeTakenByASecondCustomerAfterItWasMerged()
    {
        GuestSession guest = await CreateGuestWithItemsAsync(("tee-blue-m", 2));
        using HttpClient firstCustomer = _api.Customer(Guid.NewGuid());
        using HttpClient secondCustomer = _api.Customer(Guid.NewGuid());
        await secondCustomer.PostAsync("/v1/carts", content: null, Token);
        await MergeAsync(firstCustomer, guest.Token);

        HttpResponseMessage second = await MergeAsync(secondCustomer, guest.Token);

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync<CartDto>(second)).Items.ShouldBeEmpty();
        (await ReadAsync<CartDto>(await firstCustomer.GetAsync("/v1/carts/me", Token))).Items.ShouldHaveSingleItem().Quantity.ShouldBe(2);
    }

    [Fact]
    public async Task TheMergeNeedsBothTheCustomerAndTheTokenOfTheGuestCart()
    {
        GuestSession guest = await CreateGuestWithItemsAsync(("tee-blue-m", 1));
        using HttpClient customerOnly = _api.Customer(Guid.NewGuid());
        using HttpClient anonymous = _api.Anonymous();

        HttpResponseMessage withoutGuestToken = await customerOnly.PostAsync(MergeUrl, content: null, Token);
        HttpResponseMessage withoutCustomer = await guest.Client.PostAsync(MergeUrl, content: null, Token);
        HttpResponseMessage withNothing = await anonymous.PostAsync(MergeUrl, content: null, Token);

        withoutGuestToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await withoutGuestToken.Content.ReadFromJsonAsync<JsonElement>(Token);
        problem.GetProperty("errors").EnumerateObject().Select(property => property.Name).ShouldContain("X-Cart-Token");
        withoutCustomer.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        withNothing.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnUnknownGuestTokenIsNotFound()
    {
        using HttpClient customer = _api.Customer(Guid.NewGuid());

        HttpResponseMessage response = await MergeAsync(customer, "a-token-that-no-guest-cart-has");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FiveParallelMergesOfTheSameGuestCartNeverDoubleTheItems()
    {
        GuestSession guest = await CreateGuestWithItemsAsync(("tee-blue-m", 4), ("cap-red", 2));
        Guid customerId = Guid.NewGuid();

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(async _ =>
            {
                using HttpClient client = _api.Customer(customerId);

                return await MergeAsync(client, guest.Token);
            }));

        responses.Select(response => response.StatusCode).ShouldAllBe(
            status => status == HttpStatusCode.OK || status == HttpStatusCode.Conflict);
        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBeGreaterThan(0);
        using HttpClient customer = _api.Customer(customerId);
        CartDto final = await ReadAsync<CartDto>(await customer.GetAsync("/v1/carts/me", Token));
        final.Items.Single(item => item.ProductId == "tee-blue-m").Quantity.ShouldBe(4);
        final.Items.Single(item => item.ProductId == "cap-red").Quantity.ShouldBe(2);
    }

    private static async Task<HttpResponseMessage> MergeAsync(HttpClient customer, string guestToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, MergeUrl);
        request.Headers.Add("X-Cart-Token", guestToken);

        return await customer.SendAsync(request, Token);
    }

    private static async Task<HttpResponseMessage> AddAsync(HttpClient client, Guid cartId, string productId, int quantity)
    {
        return await client.PostAsJsonAsync($"/v1/carts/{cartId}/items", new { productId, quantity }, Token);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        T? body = await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Token);

        return body ?? throw new InvalidOperationException("The response body was empty.");
    }

    private async Task<GuestSession> CreateGuestWithItemsAsync(params (string ProductId, int Quantity)[] items)
    {
        using HttpClient anonymous = _api.Anonymous();
        GuestCartCreatedResponse created = await ReadAsync<GuestCartCreatedResponse>(await anonymous.PostAsync("/v1/carts", content: null, Token));
        HttpClient guestClient = _api.Guest(created.GuestToken);

        foreach ((string productId, int quantity) in items)
        {
            await AddAsync(guestClient, created.Cart.Id, productId, quantity);
        }

        return new GuestSession(guestClient, created.Cart.Id, created.GuestToken);
    }

    private sealed class GuestSession
    {
        public GuestSession(HttpClient client, Guid cartId, string token)
        {
            Client = client;
            CartId = cartId;
            Token = token;
        }

        public HttpClient Client { get; }

        public Guid CartId { get; }

        public string Token { get; }
    }
}
