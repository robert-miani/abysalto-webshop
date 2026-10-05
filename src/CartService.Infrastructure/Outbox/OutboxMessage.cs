namespace CartService.Infrastructure.Outbox;

using System;

/// <summary>
/// An event that waits to be published. It is written in the same transaction as the change it describes. The
/// relay claims it, publishes it, and marks it as processed.
/// </summary>
internal sealed class OutboxMessage
{
    /// <summary>The id of the event. It is also the Service Bus message id, which makes duplicates detectable.</summary>
    public required Guid Id { get; init; }

    public required string Type { get; init; }

    /// <summary>The Service Bus session of the message: events with the same key are processed in order.</summary>
    public required string OrderingKey { get; init; }

    /// <summary>The event as a CloudEvents JSON document.</summary>
    public required string Payload { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>While this is in the future, a relay owns the message and no other relay claims it.</summary>
    public DateTimeOffset? LockedUntil { get; init; }

    public DateTimeOffset? ProcessedAt { get; init; }

    public int Attempts { get; init; }

    public string? LastError { get; init; }
}
