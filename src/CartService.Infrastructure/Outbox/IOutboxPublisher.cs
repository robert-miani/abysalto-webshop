namespace CartService.Infrastructure.Outbox;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Sends one outbox message to the message broker. It must not complete before the broker has accepted the
/// message, so a message is only marked as processed after it was really published.
/// </summary>
internal interface IOutboxPublisher
{
    Task PublishAsync(ClaimedMessage message, CancellationToken cancellationToken);
}
