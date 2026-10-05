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
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CartItemEndpointsTests : IDisposable
{
    private readonly TestApi _api;

    public CartItemEndpointsTests(PostgresFixture postgres)
    {
        _api = new TestApi(postgres);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _api.Dispose();
    }

    [Fact]
    public async Task AGuestAddsAProductAndGetsTheCatalogNameAndPrice()
    {
        (HttpClient client, CartDto cart) = await CreateGuestCartAsync();

        HttpResponseMessage response = await AddAsync(client, cart.Id, "tee-blue-m", 2);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        CartDto updated = await ReadAsync<CartDto>(response);
        CartItemDto item = updated.Items.ShouldHaveSingleItem();
        item.ProductId.ShouldBe("tee-blue-m");
        item.ProductName.ShouldBe("Blue T-shirt M");
        item.UnitPrice.ShouldBe(19.90m);
        item.Quantity.ShouldBe(2);
        item.LineTotal.ShouldBe(39.80m);
        updated.Total.ShouldBe(39.80m);
        updated.UpdatedAt.ShouldBeGreaterThan(cart.UpdatedAt.AddSeconds(-1));
    }

    [Fact]
    public async Task ACustomerCanAddSeveralProductsAndAddingTheSameOneAgainAddsUp()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();

        await AddAsync(client, cart.Id, "tee-blue-m", 1);
        await AddAsync(client, cart.Id, "cap-red", 3);
        CartDto updated = await ReadAsync<CartDto>(await AddAsync(client, cart.Id, "tee-blue-m", 2));

        updated.Items.Select(item => item.ProductId).ToArray().ShouldBe(new[] { "cap-red", "tee-blue-m" });
        updated.Items.Single(item => item.ProductId == "tee-blue-m").Quantity.ShouldBe(3);
        updated.Total.ShouldBe((3 * 19.90m) + (3 * 9.50m));
    }

    [Fact]
    public async Task TheClientCannotSetThePrice()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/v1/carts/{cart.Id}/items",
            new { productId = "tee-blue-m", quantity = 1, unitPrice = 0.01m, productName = "Free T-shirt" },
            Token);

        CartDto updated = await ReadAsync<CartDto>(response);
        CartItemDto item = updated.Items.ShouldHaveSingleItem();
        item.UnitPrice.ShouldBe(19.90m);
        item.ProductName.ShouldBe("Blue T-shirt M");
    }

    [Fact]
    public async Task ChangingTheQuantityAndRemovingAProductWork()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();
        await AddAsync(client, cart.Id, "tee-blue-m", 2);
        await AddAsync(client, cart.Id, "cap-red", 1);

        HttpResponseMessage changed = await client.PutAsJsonAsync($"/v1/carts/{cart.Id}/items/tee-blue-m", new { quantity = 7 }, Token);
        HttpResponseMessage removed = await client.DeleteAsync($"/v1/carts/{cart.Id}/items/cap-red", Token);
        CartDto final = await ReadAsync<CartDto>(await client.GetAsync($"/v1/carts/{cart.Id}", Token));

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync<CartDto>(changed)).Items.Single(item => item.ProductId == "tee-blue-m").Quantity.ShouldBe(7);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        final.Items.ShouldHaveSingleItem().ProductId.ShouldBe("tee-blue-m");
    }

    [Fact]
    public async Task RemovingAProductTwiceOrOneThatIsNotThereIsNotAnError()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();
        await AddAsync(client, cart.Id, "tee-blue-m", 1);

        HttpResponseMessage first = await client.DeleteAsync($"/v1/carts/{cart.Id}/items/tee-blue-m", Token);
        HttpResponseMessage second = await client.DeleteAsync($"/v1/carts/{cart.Id}/items/tee-blue-m", Token);
        HttpResponseMessage unknown = await client.DeleteAsync($"/v1/carts/{cart.Id}/items/never-added", Token);

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AnUnknownProductIsRejectedWithItsRuleCode()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();

        HttpResponseMessage response = await AddAsync(client, cart.Id, "does-not-exist", 1);

        await ShouldBeProblemAsync(response, HttpStatusCode.UnprocessableEntity, "cart.product_not_found");
    }

    [Fact]
    public async Task ChangingTheQuantityOfAProductThatIsNotInTheCartIsRejected()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();

        HttpResponseMessage response = await client.PutAsJsonAsync($"/v1/carts/{cart.Id}/items/tee-blue-m", new { quantity = 1 }, Token);

        await ShouldBeProblemAsync(response, HttpStatusCode.UnprocessableEntity, "cart.item_not_found");
    }

    [Fact]
    public async Task MoreThanTwentyUnitsOfOneProductIsRejectedAndTheCartKeepsItsContent()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();
        await AddAsync(client, cart.Id, "mug-white", 20);

        HttpResponseMessage response = await AddAsync(client, cart.Id, "mug-white", 1);

        await ShouldBeProblemAsync(response, HttpStatusCode.UnprocessableEntity, "cart.quantity_out_of_range");
        CartDto unchanged = await ReadAsync<CartDto>(await client.GetAsync($"/v1/carts/{cart.Id}", Token));
        unchanged.Items.ShouldHaveSingleItem().Quantity.ShouldBe(20);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(21)]
    public async Task AQuantityOutsideTheLimitsIsABadRequestWithValidationDetails(int quantity)
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();

        HttpResponseMessage response = await AddAsync(client, cart.Id, "tee-blue-m", quantity);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        problem.GetProperty("errors").EnumerateObject().Select(property => property.Name.ToLowerInvariant()).ShouldContain("quantity");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankProductIdIsABadRequest(string productId)
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();

        HttpResponseMessage response = await AddAsync(client, cart.Id, productId, 1);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AProductIdThatIsTooLongIsABadRequest()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();

        HttpResponseMessage response = await AddAsync(client, cart.Id, new string('x', 65), 1);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AMissingBodyIsABadRequest()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();

        HttpResponseMessage response = await client.PostAsync($"/v1/carts/{cart.Id}/items", content: null, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NobodyCanChangeTheCartOfSomebodyElse()
    {
        (HttpClient owner, CartDto cart) = await CreateCustomerCartAsync();
        await AddAsync(owner, cart.Id, "tee-blue-m", 1);
        using HttpClient otherCustomer = _api.Customer(Guid.NewGuid());
        (HttpClient otherGuest, _) = await CreateGuestCartAsync();
        using HttpClient anonymous = _api.Anonymous();

        foreach (HttpClient attacker in new[] { otherCustomer, otherGuest })
        {
            (await AddAsync(attacker, cart.Id, "cap-red", 1)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await attacker.PutAsJsonAsync($"/v1/carts/{cart.Id}/items/tee-blue-m", new { quantity = 9 }, Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await attacker.DeleteAsync($"/v1/carts/{cart.Id}/items/tee-blue-m", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await AddAsync(anonymous, cart.Id, "cap-red", 1)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        CartDto unchanged = await ReadAsync<CartDto>(await owner.GetAsync($"/v1/carts/{cart.Id}", Token));
        unchanged.Items.ShouldHaveSingleItem().Quantity.ShouldBe(1);
    }

    [Fact]
    public async Task FiveParallelChangesToOneCartNeverFailWithAServerErrorAndNeverLoseAnAcceptedChange()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();
        string[] products = { "tee-blue-m", "tee-blue-l", "cap-red", "socks-wool", "mug-white" };

        HttpResponseMessage[] responses = await Task.WhenAll(products.Select(product => AddAsync(client, cart.Id, product, 1)));

        responses.Select(response => response.StatusCode).ShouldAllBe(status => status == HttpStatusCode.OK || status == HttpStatusCode.Conflict);
        int accepted = responses.Count(response => response.StatusCode == HttpStatusCode.OK);
        accepted.ShouldBeGreaterThan(0);
        CartDto final = await ReadAsync<CartDto>(await client.GetAsync($"/v1/carts/{cart.Id}", Token));
        final.Items.Count.ShouldBe(accepted);

        foreach (HttpResponseMessage conflict in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
        {
            JsonElement problem = await conflict.Content.ReadFromJsonAsync<JsonElement>(Token);
            problem.GetProperty("code").GetString().ShouldBe("cart.concurrency_conflict");
        }
    }

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        problem.GetProperty("code").GetString().ShouldBe(code);
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

    private async Task<(HttpClient Client, CartDto Cart)> CreateCustomerCartAsync()
    {
        HttpClient client = _api.Customer(Guid.NewGuid());
        CartDto cart = await ReadAsync<CartDto>(await client.PostAsync("/v1/carts", content: null, Token));

        return (client, cart);
    }

    private async Task<(HttpClient Client, CartDto Cart)> CreateGuestCartAsync()
    {
        using HttpClient anonymous = _api.Anonymous();
        GuestCartCreatedResponse created = await ReadAsync<GuestCartCreatedResponse>(await anonymous.PostAsync("/v1/carts", content: null, Token));

        return (_api.Guest(created.GuestToken), created.Cart);
    }
}
