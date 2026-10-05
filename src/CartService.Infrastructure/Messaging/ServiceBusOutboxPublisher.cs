namespace CartService.Infrastructure.Messaging;

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using CartService.Infrastructure.Outbox;
using Microsoft.Extensions.Options;

/// <summary>
/// Publishes outbox messages to a Service Bus topic. The message id is the event id, so the broker can drop a
/// duplicate that the relay sends again after a crash, and the session id is the ordering key, so all events of
/// one checkout are handled in order by one consumer.
/// </summary>
internal sealed class ServiceBusOutboxPublisher : IOutboxPublisher, IAsyncDisposable
{
    /// <summary>The structured JSON mode of CloudEvents.</summary>
    private const string CloudEventsContentType = "application/cloudevents+json";

    private readonly Lazy<ServiceBusClient> _client;
    private readonly Lazy<ServiceBusSender> _sender;

    public ServiceBusOutboxPublisher(IOptions<ServiceBusOptions> options)
    {
        // The client is created when the first message is published, so a disabled relay never needs a broker.
        _client = new Lazy<ServiceBusClient>(() => CreateClient(options.Value));
        _sender = new Lazy<ServiceBusSender>(() => _client.Value.CreateSender(options.Value.TopicName));
    }

    public async Task PublishAsync(ClaimedMessage message, CancellationToken cancellationToken)
    {
        ServiceBusMessage serviceBusMessage = new ServiceBusMessage(BinaryData.FromString(message.Payload))
        {
            MessageId = message.Id.ToString(),
            SessionId = message.OrderingKey,
            Subject = message.Type,
            ContentType = CloudEventsContentType,
        };

        await _sender.Value.SendMessageAsync(serviceBusMessage, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_sender.IsValueCreated)
        {
            await _sender.Value.DisposeAsync();
        }

        if (_client.IsValueCreated)
        {
            await _client.Value.DisposeAsync();
        }
    }

    private static ServiceBusClient CreateClient(ServiceBusOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new ServiceBusClient(options.ConnectionString);
        }

        // In Azure the service signs in with its workload identity: there is no secret to store or rotate.
        return new ServiceBusClient(options.FullyQualifiedNamespace, new DefaultAzureCredential());
    }
}
