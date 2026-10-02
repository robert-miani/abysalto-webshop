namespace CartService.Api.IntegrationTests.Messaging;

using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Shouldly;
using Testcontainers.ServiceBus;
using Xunit;

[Trait("Category", "Integration")]
public sealed class ServiceBusEmulatorTests : IAsyncLifetime
{
    private const string TopicName = "cart-events";
    private const string SubscriptionName = "order-service";

    private readonly ServiceBusContainer _serviceBus = new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1")
        .WithAcceptLicenseAgreement(true)
        .WithConfig(Path.Combine(AppContext.BaseDirectory, "ServiceBus", "Config.json"))
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _serviceBus.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceBus.DisposeAsync();
    }

    [Fact]
    public async Task MessageSentToCartEventsTopicReachesOrderServiceSubscription()
    {
        await using ServiceBusClient client = new ServiceBusClient(_serviceBus.GetConnectionString());
        ServiceBusSender sender = client.CreateSender(TopicName);
        ServiceBusReceiver receiver = client.CreateReceiver(TopicName, SubscriptionName);
        ServiceBusMessage message = new ServiceBusMessage(BinaryData.FromString("{}"))
        {
            MessageId = Guid.NewGuid().ToString(),
            Subject = "CartCheckedOut",
        };

        await sender.SendMessageAsync(message, TestContext.Current.CancellationToken);
        ServiceBusReceivedMessage? received = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        received.ShouldNotBeNull();
        received.Subject.ShouldBe("CartCheckedOut");
        received.MessageId.ShouldBe(message.MessageId);
    }
}
