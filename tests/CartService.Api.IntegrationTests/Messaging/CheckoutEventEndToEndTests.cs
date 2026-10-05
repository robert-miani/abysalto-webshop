namespace CartService.Api.IntegrationTests.Messaging;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using CartService.Api.IntegrationTests.Infrastructure;
using CartService.Api.IntegrationTests.Persistence;
using CartService.Application.Carts;
using CartService.Infrastructure.Outbox;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

/// <summary>
/// The whole path of a checkout: HTTP request, database transaction with the outbox row, relay, Azure Service Bus
/// emulator, and a consumer that receives the CloudEvent, like the Order service will.
/// </summary>
[Collection(MessagingCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CheckoutEventEndToEndTests : IDisposable
{
    private readonly PostgresFixture _postgres;
    private readonly ServiceBusFixture _serviceBus;
    private readonly TestApi _api;

    public CheckoutEventEndToEndTests(PostgresFixture postgres, ServiceBusFixture serviceBus)
    {
        _postgres = postgres;
        _serviceBus = serviceBus;
        _api = new TestApi(postgres, configure: builder =>
        {
            builder.UseSetting("Outbox:Enabled", "true");
            builder.UseSetting("Outbox:PollInterval", "00:00:00.100");
            builder.UseSetting("ServiceBus:ConnectionString", serviceBus.ConnectionString);
        });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _api.Dispose();
    }

    [Fact]
    public async Task CheckoutReachesTheOrderServiceSubscriptionAsACloudEventInTheSessionOfTheCheckout()
    {
        Guid customerId = Guid.NewGuid();
        using HttpClient client = _api.Customer(customerId);
        CartDto cart = (await (await client.PostAsync("/v1/carts", content: null, Token)).Content.ReadFromJsonAsync<CartDto>(TestJson.Options, Token))!;
        await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "tee-blue-m", quantity = 2 }, Token);
        await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "cap-red", quantity = 1 }, Token);

        HttpResponseMessage checkout = await client.PostAsync($"/v1/carts/{cart.Id}/checkout", content: null, Token);

        checkout.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        Guid checkoutId = (await checkout.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("checkoutId").GetGuid();

        await using ServiceBusClient consumer = new ServiceBusClient(_serviceBus.ConnectionString);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        await using ServiceBusSessionReceiver receiver = await consumer.AcceptSessionAsync(
            ServiceBusFixture.TopicName,
            ServiceBusFixture.SubscriptionName,
            checkoutId.ToString(),
            cancellationToken: timeout.Token);
        ServiceBusReceivedMessage? message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30), timeout.Token);

        message.ShouldNotBeNull();
        message.SessionId.ShouldBe(checkoutId.ToString());
        message.Subject.ShouldBe("CartCheckedOut");
        message.ContentType.ShouldBe("application/cloudevents+json");

        using JsonDocument document = JsonDocument.Parse(message.Body.ToString());
        JsonElement envelope = document.RootElement;
        envelope.GetProperty("specversion").GetString().ShouldBe("1.0");
        envelope.GetProperty("id").GetGuid().ToString().ShouldBe(message.MessageId);
        envelope.GetProperty("type").GetString().ShouldBe("CartCheckedOut");
        envelope.GetProperty("subject").GetString().ShouldBe(checkoutId.ToString());
        JsonElement data = envelope.GetProperty("data");
        data.GetProperty("checkoutId").GetGuid().ShouldBe(checkoutId);
        data.GetProperty("cartId").GetGuid().ShouldBe(cart.Id);
        data.GetProperty("customerId").GetGuid().ShouldBe(customerId);
        data.GetProperty("indicativeTotal").GetDecimal().ShouldBe(49.30m);
        data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("productId").GetString()).ToArray()
            .ShouldBe(new[] { "cap-red", "tee-blue-m" });

        await receiver.CompleteMessageAsync(message, timeout.Token);
        ServiceBusReceivedMessage? duplicate = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(3), timeout.Token);
        duplicate.ShouldBeNull();
    }

    [Fact]
    public async Task AfterPublishingTheOutboxMessageIsMarkedAsProcessed()
    {
        using HttpClient client = _api.Customer(Guid.NewGuid());
        CartDto cart = (await (await client.PostAsync("/v1/carts", content: null, Token)).Content.ReadFromJsonAsync<CartDto>(TestJson.Options, Token))!;
        await client.PostAsJsonAsync($"/v1/carts/{cart.Id}/items", new { productId = "mug-white", quantity = 1 }, Token);

        HttpResponseMessage checkout = await client.PostAsync($"/v1/carts/{cart.Id}/checkout", content: null, Token);
        Guid checkoutId = (await checkout.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("checkoutId").GetGuid();

        OutboxMessage processed = await WaitForProcessedAsync(checkoutId);

        processed.ProcessedAt.ShouldNotBeNull();
        processed.LockedUntil.ShouldBeNull();
        processed.Attempts.ShouldBe(1);
        processed.LastError.ShouldBeNull();
    }

    private async Task<OutboxMessage> WaitForProcessedAsync(Guid checkoutId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (true)
        {
            await using CartDbContext context = _postgres.CreateContext();
            OutboxMessage? message = await context.OutboxMessages.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.OrderingKey == checkoutId.ToString(), Token);

            if (message?.ProcessedAt is not null || DateTimeOffset.UtcNow > deadline)
            {
                return message ?? throw new InvalidOperationException("The outbox message does not exist.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), Token);
        }
    }
}
