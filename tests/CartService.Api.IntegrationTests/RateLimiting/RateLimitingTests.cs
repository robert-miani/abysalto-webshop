namespace CartService.Api.IntegrationTests.RateLimiting;

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Infrastructure;
using CartService.Api.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RateLimitingTests
{
    private readonly PostgresFixture _postgres;

    public RateLimitingTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACustomerWhoSendsMoreRequestsThanTheBucketHoldsGetsTooManyRequests()
    {
        using TestApi api = CreateApi(defaultTokens: 3, strictTokens: 10);
        using HttpClient customer = api.Customer(Guid.NewGuid());

        HttpStatusCode[] statuses = await GetMyCartAsync(customer, 5);

        statuses.Take(3).ShouldAllBe(status => status == HttpStatusCode.NotFound);
        statuses.Skip(3).ShouldAllBe(status => status == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheRejectionIsAProblemWithAStableCodeAndRetryAfter()
    {
        using TestApi api = CreateApi(defaultTokens: 1, strictTokens: 10);
        using HttpClient customer = api.Customer(Guid.NewGuid());
        await customer.GetAsync("/v1/carts/me", Token);

        HttpResponseMessage rejected = await customer.GetAsync("/v1/carts/me", Token);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeGreaterThan(0);
        JsonElement problem = await rejected.Content.ReadFromJsonAsync<JsonElement>(Token);
        problem.GetProperty("code").GetString().ShouldBe("rate_limit.exceeded");
        problem.GetProperty("status").GetInt32().ShouldBe(429);
    }

    [Fact]
    public async Task CustomersHaveBucketsOfTheirOwn()
    {
        using TestApi api = CreateApi(defaultTokens: 2, strictTokens: 10);
        using HttpClient busy = api.Customer(Guid.NewGuid());
        using HttpClient quiet = api.Customer(Guid.NewGuid());

        HttpStatusCode[] busyStatuses = await GetMyCartAsync(busy, 3);
        HttpStatusCode[] quietStatuses = await GetMyCartAsync(quiet, 2);

        busyStatuses.Last().ShouldBe(HttpStatusCode.TooManyRequests);
        quietStatuses.ShouldAllBe(status => status == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreatingCartsHasTheStricterLimit()
    {
        using TestApi api = CreateApi(defaultTokens: 100, strictTokens: 2);
        using HttpClient anonymous = api.Anonymous();

        HttpStatusCode[] statuses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => PostCartAsync(anonymous)));

        statuses.Count(status => status == HttpStatusCode.Created).ShouldBe(2);
        statuses.Count(status => status == HttpStatusCode.TooManyRequests).ShouldBe(1);
    }

    [Fact]
    public async Task CheckoutHasTheStricterLimit()
    {
        using TestApi api = CreateApi(defaultTokens: 100, strictTokens: 1);
        using HttpClient customer = api.Customer(Guid.NewGuid());

        // The first request spends the only strict token, so the checkout of an empty cart is rejected by the
        // limit before it can answer that the cart is empty.
        HttpResponseMessage create = await customer.PostAsync("/v1/carts", content: null, Token);
        HttpResponseMessage checkout = await customer.PostAsync($"/v1/carts/{Guid.NewGuid()}/checkout", content: null, Token);

        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        checkout.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task SpendingTheStrictBucketLeavesTheNormalBucketAlone()
    {
        using TestApi api = CreateApi(defaultTokens: 3, strictTokens: 1);
        using HttpClient customer = api.Customer(Guid.NewGuid());
        await customer.PostAsync("/v1/carts", content: null, Token);
        (await customer.PostAsync("/v1/carts", content: null, Token)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        HttpStatusCode[] statuses = await GetMyCartAsync(customer, 3);

        statuses.ShouldAllBe(status => status == HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnonymousVisitorsShareTheBucketOfTheirAddress()
    {
        using TestApi api = CreateApi(defaultTokens: 2, strictTokens: 10);
        using HttpClient first = api.Anonymous();
        using HttpClient second = api.Anonymous();

        // Without a valid token nothing identifies a visitor but the address, and a guest cart token is not
        // trusted as a key, so changing it does not give a visitor a new bucket.
        HttpStatusCode[] statuses = new[]
        {
            await GetAnonymousAsync(first, "token-1"),
            await GetAnonymousAsync(second, "token-2"),
            await GetAnonymousAsync(first, "token-3"),
        };

        statuses.Last().ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task HealthChecksAreNeverLimited()
    {
        using TestApi api = CreateApi(defaultTokens: 1, strictTokens: 1);
        using HttpClient anonymous = api.Anonymous();

        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => anonymous.GetAsync("/health/live", Token)));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task WhenTheLimitsAreSwitchedOffNothingIsRejected()
    {
        using TestApi api = new TestApi(_postgres);
        using HttpClient anonymous = api.Anonymous();

        HttpStatusCode[] statuses = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => PostCartAsync(anonymous)));

        statuses.ShouldAllBe(status => status == HttpStatusCode.Created);
    }

    private static async Task<HttpStatusCode[]> GetMyCartAsync(HttpClient client, int count)
    {
        HttpStatusCode[] statuses = new HttpStatusCode[count];

        for (int index = 0; index < count; index++)
        {
            statuses[index] = (await client.GetAsync("/v1/carts/me", Token)).StatusCode;
        }

        return statuses;
    }

    private static async Task<HttpStatusCode> PostCartAsync(HttpClient client)
    {
        return (await client.PostAsync("/v1/carts", content: null, Token)).StatusCode;
    }

    private static async Task<HttpStatusCode> GetAnonymousAsync(HttpClient client, string guestToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"/v1/carts/{Guid.NewGuid()}");
        request.Headers.Add("X-Cart-Token", guestToken);

        return (await client.SendAsync(request, Token)).StatusCode;
    }

    private TestApi CreateApi(int defaultTokens, int strictTokens)
    {
        return new TestApi(_postgres, configure: builder => ConfigureLimits(builder, defaultTokens, strictTokens));
    }

    // The buckets refill once an hour, so a test sees a fixed number of tokens however slowly it runs.
    private static void ConfigureLimits(IWebHostBuilder builder, int defaultTokens, int strictTokens)
    {
        builder.UseSetting("RateLimiting:Enabled", "true");
        builder.UseSetting("RateLimiting:Default:TokenLimit", defaultTokens.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimiting:Default:TokensPerPeriod", "1");
        builder.UseSetting("RateLimiting:Default:ReplenishmentPeriod", "01:00:00");
        builder.UseSetting("RateLimiting:Strict:TokenLimit", strictTokens.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimiting:Strict:TokensPerPeriod", "1");
        builder.UseSetting("RateLimiting:Strict:ReplenishmentPeriod", "01:00:00");
    }
}
