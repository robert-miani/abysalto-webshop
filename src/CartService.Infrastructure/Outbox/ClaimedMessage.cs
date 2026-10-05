namespace CartService.Infrastructure.Outbox;

using System;

/// <summary>
/// A message that a relay has claimed and must now publish.
/// </summary>
internal sealed class ClaimedMessage
{
    public required Guid Id { get; init; }

    public required string Type { get; init; }

    public required string OrderingKey { get; init; }

    public required string Payload { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The number of this attempt, counting from 1.</summary>
    public required int Attempt { get; init; }
}
