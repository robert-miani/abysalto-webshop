namespace CartService.Api.IntegrationTests.Outbox;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CartService.Infrastructure.Outbox;

/// <summary>
/// Records what the relay publishes, and fails on demand to test retries.
/// </summary>
internal sealed class FakeOutboxPublisher : IOutboxPublisher
{
    private readonly ConcurrentQueue<ClaimedMessage> _published = new ConcurrentQueue<ClaimedMessage>();

    /// <summary>When set, every publish throws this exception instead of recording the message.</summary>
    public Exception? FailWith { get; set; }

    public IReadOnlyList<ClaimedMessage> Published => _published.ToList();

    public Task PublishAsync(ClaimedMessage message, CancellationToken cancellationToken)
    {
        if (FailWith is not null)
        {
            return Task.FromException(FailWith);
        }

        _published.Enqueue(message);

        return Task.CompletedTask;
    }
}
