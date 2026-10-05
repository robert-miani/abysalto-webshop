namespace CartService.Api.IntegrationTests.Observability;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Infrastructure;
using CartService.Api.IntegrationTests.Persistence;
using CartService.Application.Carts;
using CartService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ObservabilityTests : IDisposable
{
    private readonly LockedCollection<Activity> _spans = new LockedCollection<Activity>();
    private readonly LockedCollection<Metric> _metrics = new LockedCollection<Metric>();
    private readonly PostgresFixture _postgres;
    private readonly TestApi _api;

    public ObservabilityTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        _api = new TestApi(postgres, configure: UseInMemoryExporters);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _api.Dispose();
    }

    [Fact]
    public async Task ACartRequestIsATraceWithTheRouteAndTheDatabaseCalls()
    {
        using HttpClient client = _api.Customer(Guid.NewGuid());

        await client.PostAsync("/v1/carts", content: null, Token);

        Activity server = await WaitForServerSpanAsync(span => span.DisplayName.StartsWith("POST /v1/carts", StringComparison.Ordinal));
        server.Kind.ShouldBe(ActivityKind.Server);
        server.GetTagItem("http.response.status_code").ShouldBe(201);
        // The span is named after the route template, never after the path with ids in it.
        server.GetTagItem("http.route").ShouldBeOfType<string>().ShouldStartWith("/v1/carts");
        SpansOf("Npgsql").Where(span => span.TraceId == server.TraceId).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ARequestThatCarriesATraceparentContinuesThatTrace()
    {
        using HttpClient client = _api.Customer(Guid.NewGuid());
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/v1/carts");
        request.Headers.Add("traceparent", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");

        await client.SendAsync(request, Token);

        Activity server = await WaitForServerSpanAsync(span => span.TraceId.ToString() == "0af7651916cd43dd8448eb211c80319c");
        server.ParentSpanId.ToString().ShouldBe("b7ad6b7169203331");
    }

    [Fact]
    public async Task HealthProbesAreNotTraced()
    {
        using HttpClient client = _api.Anonymous();

        await client.GetAsync("/health/live", Token);
        await client.GetAsync("/health/ready", Token);

        // A request after the probes proves that spans of earlier requests have been exported by now.
        using HttpClient customer = _api.Customer(Guid.NewGuid());
        await customer.GetAsync("/v1/carts/me", Token);
        await WaitForServerSpanAsync(span => span.DisplayName.StartsWith("GET /v1/carts/me", StringComparison.Ordinal));

        SpansOf("Microsoft.AspNetCore").ShouldNotContain(span => span.DisplayName.Contains("health", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NoSpanContainsATokenOrAnAuthorizationHeader()
    {
        Guid customerId = Guid.NewGuid();
        string bearer = TestTokens.ForCustomer(customerId);
        using HttpClient anonymous = _api.Anonymous();
        GuestBody created = (await (await anonymous.PostAsync("/v1/carts", content: null, Token)).Content.ReadFromJsonAsync<GuestBody>(TestJson.Options, Token))!;
        using HttpClient guest = _api.Guest(created.GuestToken);
        using HttpClient customer = _api.CustomerWithToken(bearer);

        await guest.GetAsync($"/v1/carts/{created.Cart.Id}", Token);
        await customer.GetAsync("/v1/carts/me", Token);
        using HttpRequestMessage merge = new HttpRequestMessage(HttpMethod.Post, "/v1/carts/me/merge");
        merge.Headers.Add("X-Cart-Token", created.GuestToken);
        await customer.SendAsync(merge, Token);
        await WaitForServerSpanAsync(span => span.DisplayName.EndsWith("/me/merge", StringComparison.Ordinal));

        string[] values = _spans
            .SelectMany(span => span.TagObjects.Select(tag => tag.Value?.ToString() ?? string.Empty).Concat(span.Events.SelectMany(spanEvent => spanEvent.Tags.Select(tag => tag.Value?.ToString() ?? string.Empty))))
            .ToArray();

        values.ShouldNotBeEmpty();
        values.ShouldNotContain(value => value.Contains(created.GuestToken, StringComparison.Ordinal));
        values.ShouldNotContain(value => value.Contains(bearer, StringComparison.Ordinal));
        values.ShouldNotContain(value => value.Contains("Bearer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheTraceOfACheckoutContinuesInTheEventForTheOrderService()
    {
        using HttpClient client = _api.Customer(Guid.NewGuid());
        CartDto cart = (await (await client.PostAsync("/v1/carts", content: null, Token)).Content.ReadFromJsonAsync<CartDto>(TestJson.Options, Token))!;
        await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "cap-red", quantity = 1 }, Token);

        await client.PostAsync($"/v1/carts/{cart.Id}/checkout", content: null, Token);

        Activity server = await WaitForServerSpanAsync(span => span.DisplayName.EndsWith("/checkout", StringComparison.Ordinal));
        string payload = await ReadEventPayloadAsync(cart.Id);
        JsonElement envelope = JsonSerializer.Deserialize<JsonElement>(payload);
        ActivityContext.TryParse(envelope.GetProperty("traceparent").GetString(), null, out ActivityContext carried).ShouldBeTrue();
        carried.TraceId.ShouldBe(server.TraceId);
    }

    [Fact]
    public async Task TheCartMetricsAreExported()
    {
        using HttpClient client = _api.Customer(Guid.NewGuid());
        CartDto cart = (await (await client.PostAsync("/v1/carts", content: null, Token)).Content.ReadFromJsonAsync<CartDto>(TestJson.Options, Token))!;
        await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "cap-red", quantity = 2 }, Token);
        await client.GetAsync($"/v1/carts/{cart.Id}", Token);

        // The request metrics are recorded when the request ends, which can be after the client has its answer.
        await EventuallyAsync(() =>
        {
            _api.Services.GetRequiredService<MeterProvider>().ForceFlush();

            return _metrics.Any(metric => metric.Name == "http.server.request.duration");
        });

        string[] names = _metrics.Select(metric => metric.Name).Distinct().ToArray();
        names.ShouldContain("cartservice.carts.created");
        names.ShouldContain("cartservice.items.added");
        names.ShouldContain("cartservice.cache.lookups");
        names.ShouldContain("http.server.request.duration");
        names.ShouldContain(name => name.StartsWith("dotnet.", StringComparison.Ordinal));
    }

    private void UseInMemoryExporters(IWebHostBuilder builder)
    {
        // Nothing leaves the process: the spans and metrics are collected in memory, for this host only.
        builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
        builder.ConfigureServices(services =>
        {
            services.ConfigureOpenTelemetryTracerProvider((_, tracing) => tracing.AddInMemoryExporter(_spans));
            services.ConfigureOpenTelemetryMeterProvider((_, metrics) => metrics.AddInMemoryExporter(_metrics));
        });
    }

    private async Task<string> ReadEventPayloadAsync(Guid cartId)
    {
        await using CartDbContext context = _postgres.CreateContext();

        return await context.Database
            .SqlQuery<string>($"SELECT payload::text AS \"Value\" FROM outbox_messages WHERE payload->'data'->>'cartId' = {cartId.ToString()}")
            .SingleAsync(Token);
    }

    private Activity[] SpansOf(string sourceName)
    {
        return _spans.Where(span => span.Source.Name == sourceName).ToArray();
    }

    // The span of a request ends after the response is written, so the client can have its answer before the span
    // is exported.
    private async Task<Activity> WaitForServerSpanAsync(Func<Activity, bool> predicate)
    {
        Activity? found = null;
        await EventuallyAsync(() =>
        {
            found = SpansOf("Microsoft.AspNetCore").FirstOrDefault(predicate);

            return found is not null;
        });

        return found!;
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The telemetry did not arrive in time.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
        }
    }

    private sealed class GuestBody
    {
        public required string GuestToken { get; init; }

        public required CartDto Cart { get; init; }
    }
}
