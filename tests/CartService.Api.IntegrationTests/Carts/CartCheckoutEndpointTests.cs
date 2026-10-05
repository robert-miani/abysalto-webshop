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
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CartCheckoutEndpointTests : IDisposable
{
    private readonly PostgresFixture _postgres;
    private readonly TestApi _api;

    public CartCheckoutEndpointTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        _api = new TestApi(postgres);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _api.Dispose();
    }

    [Fact]
    public async Task CheckoutIsAcceptedAndTheCartBecomesReadOnly()
    {
        (HttpClient client, CartDto cart) = await CreateCartWithItemsAsync();

        HttpResponseMessage response = await client.PostAsync($"/v1/carts/{cart.Id}/checkout", content: null, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Guid checkoutId = body.GetProperty("checkoutId").GetGuid();
        checkoutId.ShouldNotBe(Guid.Empty);
        CartDto afterwards = await ReadAsync<CartDto>(await client.GetAsync($"/v1/carts/{cart.Id}", Token));
        afterwards.Status.ShouldBe(CartStatus.CheckoutPending);
        afterwards.Items.Count.ShouldBe(2);
        (await CountMessagesAsync(checkoutId)).ShouldBe(1);
    }

    [Fact]
    public async Task ACartInCheckoutRejectsEveryChange()
    {
        (HttpClient client, CartDto cart) = await CreateCartWithItemsAsync();
        await client.PostAsync($"/v1/carts/{cart.Id}/checkout", content: null, Token);

        HttpResponseMessage add = await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "mug-white", quantity = 1 }, Token);
        HttpResponseMessage change = await client.PutAsJsonAsync($"/v1/carts/{cart.Id}/items/tee-blue-m", new { quantity = 5 }, Token);
        HttpResponseMessage remove = await client.DeleteAsync($"/v1/carts/{cart.Id}/items/tee-blue-m", Token);

        foreach (HttpResponseMessage response in new[] { add, change, remove })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
            problem.GetProperty("code").GetString().ShouldBe("cart.not_active");
        }
    }

    [Fact]
    public async Task RepeatingCheckoutAnswersWithTheSameCheckoutAndStoresNoSecondEvent()
    {
        (HttpClient client, CartDto cart) = await CreateCartWithItemsAsync();

        Guid first = await CheckoutAsync(client, cart.Id);
        Guid second = await CheckoutAsync(client, cart.Id);

        second.ShouldBe(first);
        (await CountMessagesAsync(first)).ShouldBe(1);
    }

    [Fact]
    public async Task AfterCheckoutTheCustomerStartsANewActiveCart()
    {
        (HttpClient client, CartDto cart) = await CreateCartWithItemsAsync();
        await CheckoutAsync(client, cart.Id);

        HttpResponseMessage response = await client.PostAsync("/v1/carts", content: null, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        CartDto fresh = await ReadAsync<CartDto>(response);
        fresh.Id.ShouldNotBe(cart.Id);
        fresh.Status.ShouldBe(CartStatus.Active);
        fresh.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task FiveParallelCheckoutsOfTheSameCartStoreExactlyOneEvent()
    {
        (HttpClient client, CartDto cart) = await CreateCartWithItemsAsync();

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => client.PostAsync($"/v1/carts/{cart.Id}/checkout", content: null, Token)));

        responses.Select(response => response.StatusCode).ShouldAllBe(
            status => status == HttpStatusCode.Accepted || status == HttpStatusCode.Conflict);
        HttpResponseMessage[] accepted = responses.Where(response => response.StatusCode == HttpStatusCode.Accepted).ToArray();
        accepted.Length.ShouldBeGreaterThan(0);
        Guid[] checkoutIds = new Guid[accepted.Length];

        for (int index = 0; index < accepted.Length; index++)
        {
            JsonElement body = await accepted[index].Content.ReadFromJsonAsync<JsonElement>(Token);
            checkoutIds[index] = body.GetProperty("checkoutId").GetGuid();
        }

        checkoutIds.Distinct().Count().ShouldBe(1);
        (await CountMessagesAsync(checkoutIds[0])).ShouldBe(1);
    }

    [Fact]
    public async Task AnEmptyCartCannotBeCheckedOut()
    {
        using HttpClient client = _api.Customer(Guid.NewGuid());
        CartDto cart = await ReadAsync<CartDto>(await client.PostAsync("/v1/carts", content: null, Token));

        HttpResponseMessage response = await client.PostAsync($"/v1/carts/{cart.Id}/checkout", content: null, Token);

        await ShouldBeProblemAsync(response, HttpStatusCode.UnprocessableEntity, "cart.empty");
    }

    [Fact]
    public async Task AGuestCannotCheckOut()
    {
        using HttpClient anonymous = _api.Anonymous();
        GuestCartCreatedResponse guest = await ReadAsync<GuestCartCreatedResponse>(await anonymous.PostAsync("/v1/carts", content: null, Token));
        using HttpClient guestClient = _api.Guest(guest.GuestToken);
        await guestClient.PostAsJsonAsync($"/v1/carts/{guest.Cart.Id}/items", new { productId = "cap-red", quantity = 1 }, Token);

        HttpResponseMessage response = await guestClient.PostAsync($"/v1/carts/{guest.Cart.Id}/checkout", content: null, Token);

        await ShouldBeProblemAsync(response, HttpStatusCode.UnprocessableEntity, "cart.checkout_requires_customer");
    }

    [Fact]
    public async Task NobodyElseCanCheckOutTheCart()
    {
        (HttpClient owner, CartDto cart) = await CreateCartWithItemsAsync();
        using HttpClient otherCustomer = _api.Customer(Guid.NewGuid());
        using HttpClient anonymous = _api.Anonymous();

        (await otherCustomer.PostAsync($"/v1/carts/{cart.Id}/checkout", content: null, Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.PostAsync($"/v1/carts/{cart.Id}/checkout", content: null, Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        CartDto unchanged = await ReadAsync<CartDto>(await owner.GetAsync($"/v1/carts/{cart.Id}", Token));
        unchanged.Status.ShouldBe(CartStatus.Active);
    }

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        problem.GetProperty("code").GetString().ShouldBe(code);
    }

    private static async Task<Guid> CheckoutAsync(HttpClient client, Guid cartId)
    {
        HttpResponseMessage response = await client.PostAsync($"/v1/carts/{cartId}/checkout", content: null, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Token);

        return body.GetProperty("checkoutId").GetGuid();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        T? body = await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Token);

        return body ?? throw new InvalidOperationException("The response body was empty.");
    }

    private async Task<(HttpClient Client, CartDto Cart)> CreateCartWithItemsAsync()
    {
        HttpClient client = _api.Customer(Guid.NewGuid());
        CartDto cart = await ReadAsync<CartDto>(await client.PostAsync("/v1/carts", content: null, Token));
        await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "tee-blue-m", quantity = 2 }, Token);
        await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "cap-red", quantity = 1 }, Token);

        return (client, cart);
    }

    private async Task<int> CountMessagesAsync(Guid checkoutId)
    {
        await using CartDbContext context = _postgres.CreateContext();

        return await context.OutboxMessages.CountAsync(message => message.OrderingKey == checkoutId.ToString(), Token);
    }
}
