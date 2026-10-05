namespace CartService.Infrastructure.Outbox;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// How the relay publishes the events that wait in the outbox.
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Turns the relay off, for example in tests that do not need a message broker.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How long the relay waits before it looks for new messages when it found none.</summary>
    [Range(typeof(TimeSpan), "00:00:00.050", "00:10:00")]
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The largest number of messages that one relay claims at a time.</summary>
    [Range(1, 500)]
    public int BatchSize { get; init; } = 20;

    /// <summary>
    /// How long a claimed message belongs to one relay. If the relay dies, another one takes the message over when
    /// the lease has expired.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>After this many failed attempts a message stays in the table and raises an error log.</summary>
    [Range(1, 100)]
    public int MaxAttempts { get; init; } = 10;

    /// <summary>The wait before a failed message is tried again. It grows with every attempt.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00")]
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When the oldest message has waited this long, the readiness check reports the service as degraded, because
    /// events are not reaching Service Bus.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "1.00:00:00")]
    public TimeSpan DegradedAfter { get; init; } = TimeSpan.FromMinutes(1);
}
