namespace CartService.Api.IntegrationTests.Carts;

using System;
using System.Collections.Generic;
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
public sealed class CartEndpointsTests : IDisposable
{
    private readonly TestApi _api;

    public CartEndpointsTests(PostgresFixture postgres)
    {
        _api = new TestApi(postgres);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _api.Dispose();
    }

    [Fact]
    public async Task AnAnonymousVisitorGetsAGuestCartAndItsTokenOnce()
    {
        using HttpClient client = _api.Anonymous();

        HttpResponseMessage response = await client.PostAsync("/v1/carts", content: null, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        GuestCartCreatedResponse body = await ReadAsync<GuestCartCreatedResponse>(response);
        body.GuestToken.Length.ShouldBe(43);
        body.Cart.Status.ShouldBe(CartStatus.Active);
        body.Cart.Items.ShouldBeEmpty();
        body.Cart.Currency.ShouldBe("EUR");
        response.Headers.Location!.ToString().ShouldBe($"/v1/carts/{body.Cart.Id}");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task TheGuestCanReadTheirCartWithTheTokenAndNoOneElseCan()
    {
        GuestCartCreatedResponse guest = await CreateGuestCartAsync();
        using HttpClient withToken = _api.Guest(guest.GuestToken);
        using HttpClient withOtherToken = _api.Guest((await CreateGuestCartAsync()).GuestToken);
        using HttpClient withoutToken = _api.Anonymous();
        using HttpClient asCustomer = _api.Customer(Guid.NewGuid());

        (await withToken.GetAsync($"/v1/carts/{guest.Cart.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await withOtherToken.GetAsync($"/v1/carts/{guest.Cart.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await asCustomer.GetAsync($"/v1/carts/{guest.Cart.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await withoutToken.GetAsync($"/v1/carts/{guest.Cart.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACustomerGetsTheirCartAndThenTheSameCartAgain()
    {
        using HttpClient client = _api.Customer(Guid.NewGuid());

        HttpResponseMessage first = await client.PostAsync("/v1/carts", content: null, Token);
        HttpResponseMessage second = await client.PostAsync("/v1/carts", content: null, Token);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement raw = await first.Content.ReadFromJsonAsync<JsonElement>(Token);
        raw.TryGetProperty("guestToken", out _).ShouldBeFalse();
        CartDto created = raw.Deserialize<CartDto>(TestJson.Options)!;
        CartDto existing = await ReadAsync<CartDto>(second);
        existing.Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task ACustomerCanReadTheirOwnCartByIdAndAsMyCart()
    {
        using HttpClient client = _api.Customer(Guid.NewGuid());
        CartDto created = await ReadAsync<CartDto>(await client.PostAsync("/v1/carts", content: null, Token));

        CartDto byId = await ReadAsync<CartDto>(await client.GetAsync($"/v1/carts/{created.Id}", Token));
        CartDto mine = await ReadAsync<CartDto>(await client.GetAsync("/v1/carts/me", Token));

        byId.Id.ShouldBe(created.Id);
        mine.Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task ACustomerCannotReadAnotherCustomersCart()
    {
        using HttpClient owner = _api.Customer(Guid.NewGuid());
        using HttpClient other = _api.Customer(Guid.NewGuid());
        CartDto created = await ReadAsync<CartDto>(await owner.PostAsync("/v1/carts", content: null, Token));

        HttpResponseMessage response = await other.GetAsync($"/v1/carts/{created.Id}", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnUnknownCartIsNotFoundWithAProblemDetailsBody()
    {
        using HttpClient client = _api.Customer(Guid.NewGuid());

        HttpResponseMessage response = await client.GetAsync($"/v1/carts/{Guid.NewGuid()}", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        problem.GetProperty("code").GetString().ShouldBe("cart.not_found");
    }

    [Fact]
    public async Task ACustomerWithoutACartGetsNotFoundForMyCart()
    {
        using HttpClient client = _api.Customer(Guid.NewGuid());

        HttpResponseMessage response = await client.GetAsync("/v1/carts/me", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MyCartNeedsASignedInCustomer()
    {
        GuestCartCreatedResponse guest = await CreateGuestCartAsync();
        using HttpClient anonymous = _api.Anonymous();
        using HttpClient asGuest = _api.Guest(guest.GuestToken);

        (await anonymous.GetAsync("/v1/carts/me", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await asGuest.GetAsync("/v1/carts/me", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKeyIsRejected()
    {
        string forged = TestTokens.ForCustomer(Guid.NewGuid(), signingKey: "another-signing-key-that-is-long-enough-123456");
        using HttpClient client = _api.CustomerWithToken(forged);

        (await client.GetAsync("/v1/carts/me", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenForAnotherAudienceIsRejected()
    {
        string token = TestTokens.ForCustomer(Guid.NewGuid(), audience: "another-api");
        using HttpClient client = _api.CustomerWithToken(token);

        (await client.GetAsync("/v1/carts/me", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EightRequestsCreatingTheSameCustomerCartAtOnceAllGetTheSameCart()
    {
        Guid customerId = Guid.NewGuid();

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(async _ =>
            {
                using HttpClient client = _api.Customer(customerId);

                return await client.PostAsync("/v1/carts", content: null, Token);
            }));

        responses.Select(response => response.StatusCode).ShouldAllBe(status => status == HttpStatusCode.Created || status == HttpStatusCode.OK);
        responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        List<Guid> ids = new List<Guid>();

        foreach (HttpResponseMessage response in responses)
        {
            ids.Add((await ReadAsync<CartDto>(response)).Id);
        }

        ids.Distinct().Count().ShouldBe(1);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        T? body = await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Token);

        return body ?? throw new InvalidOperationException("The response body was empty.");
    }

    private async Task<GuestCartCreatedResponse> CreateGuestCartAsync()
    {
        using HttpClient client = _api.Anonymous();

        return await ReadAsync<GuestCartCreatedResponse>(await client.PostAsync("/v1/carts", content: null, Token));
    }
}
