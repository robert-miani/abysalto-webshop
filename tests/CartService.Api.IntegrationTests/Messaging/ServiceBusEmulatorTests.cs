namespace CartService.Api.IntegrationTests.Messaging;

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Shouldly;
using Xunit;

/// <summary>
/// Shows that the emulator works with the shared configuration: a message with a session id sent to the topic
/// reaches the session-enabled subscription.
/// </summary>
[Collection(MessagingCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ServiceBusEmulatorTests
{
    private readonly ServiceBusFixture _serviceBus;

    public ServiceBusEmulatorTests(ServiceBusFixture serviceBus)
    {
        _serviceBus = serviceBus;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MessageSentToCartEventsTopicReachesOrderServiceSubscription()
    {
        await using ServiceBusClient client = new ServiceBusClient(_serviceBus.ConnectionString);
        ServiceBusSender sender = client.CreateSender(ServiceBusFixture.TopicName);
        string sessionId = Guid.NewGuid().ToString();
        ServiceBusMessage message = new ServiceBusMessage(BinaryData.FromString("{}"))
        {
            MessageId = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            Subject = "CartCheckedOut",
        };

        await sender.SendMessageAsync(message, Token);
        await using ServiceBusSessionReceiver receiver = await client.AcceptSessionAsync(
            ServiceBusFixture.TopicName,
            ServiceBusFixture.SubscriptionName,
            sessionId,
            cancellationToken: Token);
        ServiceBusReceivedMessage? received = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30), Token);

        received.ShouldNotBeNull();
        received.Subject.ShouldBe("CartCheckedOut");
        received.MessageId.ShouldBe(message.MessageId);
        received.SessionId.ShouldBe(sessionId);
    }
}
