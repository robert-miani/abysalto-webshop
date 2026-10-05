namespace CartService.Api.IntegrationTests.Idempotency;

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
using CartService.Infrastructure.Idempotency;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IdempotentAddItemTests : IDisposable
{
    private const string KeyHeader = "Idempotency-Key";

    private readonly PostgresFixture _postgres;
    private readonly TestApi _api;

    public IdempotentAddItemTests(PostgresFixture postgres)
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
    public async Task RepeatingARequestWithTheSameKeyAddsTheProductOnce()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();
        string key = NewKey();

        HttpResponseMessage first = await AddAsync(client, cart.Id, key, "tee-blue-m", 2);
        HttpResponseMessage second = await AddAsync(client, cart.Id, key, "tee-blue-m", 2);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        CartDto current = await ReadAsync<CartDto>(await client.GetAsync($"/v1/carts/{cart.Id}", Token));
        current.Items.ShouldHaveSingleItem().Quantity.ShouldBe(2);
    }

    [Fact]
    public async Task TheRepeatedRequestGetsTheSameAnswerAndIsMarkedAsAReplay()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();
        string key = NewKey();

        HttpResponseMessage first = await AddAsync(client, cart.Id, key, "tee-blue-m", 2);
        string firstBody = await first.Content.ReadAsStringAsync(Token);
        HttpResponseMessage second = await AddAsync(client, cart.Id, key, "tee-blue-m", 2);
        string secondBody = await second.Content.ReadAsStringAsync(Token);

        first.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        second.Headers.GetValues("Idempotent-Replayed").ShouldBe(new[] { "true" });
        second.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        second.Headers.CacheControl!.NoStore.ShouldBeTrue();
        secondBody.ShouldBe(firstBody);
    }

    [Fact]
    public async Task WithoutAKeyEveryRequestIsCarriedOut()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();

        await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "tee-blue-m", quantity = 1 }, Token);
        await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "tee-blue-m", quantity = 1 }, Token);

        CartDto current = await ReadAsync<CartDto>(await client.GetAsync($"/v1/carts/{cart.Id}", Token));
        current.Items.ShouldHaveSingleItem().Quantity.ShouldBe(2);
    }

    [Fact]
    public async Task TheSameKeyForADifferentRequestIsRejectedAndChangesNothing()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();
        string key = NewKey();
        await AddAsync(client, cart.Id, key, "tee-blue-m", 1);

        HttpResponseMessage response = await AddAsync(client, cart.Id, key, "cap-red", 1);

        await ShouldBeProblemAsync(response, HttpStatusCode.UnprocessableEntity, "idempotency.key_reused");
        CartDto current = await ReadAsync<CartDto>(await client.GetAsync($"/v1/carts/{cart.Id}", Token));
        current.Items.ShouldHaveSingleItem().ProductId.ShouldBe("tee-blue-m");
    }

    [Fact]
    public async Task TheSameKeyForAnotherCartIsRejected()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();
        string key = NewKey();
        await AddAsync(client, cart.Id, key, "tee-blue-m", 1);

        HttpResponseMessage response = await AddAsync(client, Guid.NewGuid(), key, "tee-blue-m", 1);

        await ShouldBeProblemAsync(response, HttpStatusCode.UnprocessableEntity, "idempotency.key_reused");
    }

    [Fact]
    public async Task TwoRequestersCanUseTheSameKey()
    {
        (HttpClient first, CartDto firstCart) = await CreateCustomerCartAsync();
        (HttpClient second, CartDto secondCart) = await CreateCustomerCartAsync();
        string key = NewKey();

        HttpResponseMessage firstResponse = await AddAsync(first, firstCart.Id, key, "tee-blue-m", 1);
        HttpResponseMessage secondResponse = await AddAsync(second, secondCart.Id, key, "cap-red", 1);

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondResponse.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        (await ReadAsync<CartDto>(secondResponse)).Items.ShouldHaveSingleItem().ProductId.ShouldBe("cap-red");
    }

    [Fact]
    public async Task AGuestCanUseAKeyToo()
    {
        (HttpClient client, CartDto cart) = await CreateGuestCartAsync();
        string key = NewKey();

        await AddAsync(client, cart.Id, key, "cap-red", 3);
        HttpResponseMessage repeated = await AddAsync(client, cart.Id, key, "cap-red", 3);

        repeated.Headers.GetValues("Idempotent-Replayed").ShouldBe(new[] { "true" });
        (await ReadAsync<CartDto>(repeated)).Items.ShouldHaveSingleItem().Quantity.ShouldBe(3);
    }

    [Fact]
    public async Task ARejectedRequestGivesTheKeyBackSoTheClientCanTryAgain()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();
        string key = NewKey();

        HttpResponseMessage rejected = await AddAsync(client, cart.Id, key, "does-not-exist", 1);
        HttpResponseMessage again = await AddAsync(client, cart.Id, key, "does-not-exist", 1);

        await ShouldBeProblemAsync(rejected, HttpStatusCode.UnprocessableEntity, "cart.product_not_found");
        await ShouldBeProblemAsync(again, HttpStatusCode.UnprocessableEntity, "cart.product_not_found");
        again.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
    }

    [Fact]
    public async Task AKeyThatFailedValidationCanBeUsedForACorrectedRequest()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();
        string key = NewKey();

        HttpResponseMessage invalid = await AddAsync(client, cart.Id, key, "tee-blue-m", 0);
        HttpResponseMessage corrected = await AddAsync(client, cart.Id, key, "tee-blue-m", 1);

        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK);
        corrected.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
    }

    [Fact]
    public async Task ARequestThatIsStillRunningMakesTheRepeatWait()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();
        string key = NewKey();
        await AddAsync(client, cart.Id, key, "tee-blue-m", 1);
        await MakeRecordLookUnfinishedAsync(key);

        HttpResponseMessage response = await AddAsync(client, cart.Id, key, "tee-blue-m", 1);

        await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, "idempotency.request_in_progress");
        response.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task OfTenParallelRequestsWithTheSameKeyTheProductIsAddedOnce()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();
        string key = NewKey();

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => AddAsync(client, cart.Id, key, "tee-blue-m", 1)));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Conflict);
        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBeGreaterThanOrEqualTo(1);
        CartDto current = await ReadAsync<CartDto>(await client.GetAsync($"/v1/carts/{cart.Id}", Token));
        current.Items.ShouldHaveSingleItem().Quantity.ShouldBe(1);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("a,b")]
    public async Task AKeyWithUnsupportedCharactersIsRejected(string key)
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();

        HttpResponseMessage response = await AddAsync(client, cart.Id, key, "tee-blue-m", 1);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        problem.GetProperty("errors").TryGetProperty("Idempotency-Key", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task AKeyThatIsTooLongIsRejected()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();

        HttpResponseMessage response = await AddAsync(client, cart.Id, new string('k', 129), "tee-blue-m", 1);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnotherCustomerCannotReplayTheAnswerOfARequest()
    {
        (HttpClient owner, CartDto cart) = await CreateCustomerCartAsync();
        string key = NewKey();
        await AddAsync(owner, cart.Id, key, "tee-blue-m", 1);
        using HttpClient attacker = _api.Customer(Guid.NewGuid());

        HttpResponseMessage response = await AddAsync(attacker, cart.Id, key, "tee-blue-m", 1);

        await ShouldBeProblemAsync(response, HttpStatusCode.NotFound, "cart.not_found");
    }

    [Fact]
    public async Task TheKeyIsStoredForTheRequesterAndExpiresWithTheRetention()
    {
        (HttpClient client, CartDto cart) = await CreateCustomerCartAsync();
        string key = NewKey();
        await AddAsync(client, cart.Id, key, "tee-blue-m", 1);

        await using CartDbContext context = _postgres.CreateContext();
        IdempotencyRecord record = await context.IdempotencyRecords.AsNoTracking().SingleAsync(candidate => candidate.Key == key, Token);

        record.Scope.ShouldStartWith("customer:");
        record.Status.ShouldBe(IdempotencyRecord.Completed);
        record.ResponseStatusCode.ShouldBe(200);
        (record.ExpiresAt - record.CreatedAt).ShouldBeInRange(TimeSpan.FromHours(23.9), TimeSpan.FromHours(24.1));
    }

    private static string NewKey()
    {
        return Guid.NewGuid().ToString();
    }

    private static async Task<HttpResponseMessage> AddAsync(HttpClient client, Guid cartId, string key, string productId, int quantity)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"/v1/carts/{cartId}/items")
        {
            Content = JsonContent.Create(new { productId, quantity }),
        };
        request.Headers.TryAddWithoutValidation(KeyHeader, key);

        return await client.SendAsync(request, Token);
    }

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        problem.GetProperty("code").GetString().ShouldBe(code);
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

    // Plays the part of a first request that is still running: the record exists, but without an answer yet.
    private async Task MakeRecordLookUnfinishedAsync(string key)
    {
        await using CartDbContext context = _postgres.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE idempotency_records SET status = 'InProgress', response_status_code = NULL, response_body = NULL, expires_at = now() + interval '30 seconds' WHERE \"key\" = {key}",
            Token);
    }
}
