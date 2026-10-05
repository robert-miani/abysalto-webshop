namespace CartService.Infrastructure.Cleanup;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// What the cleanup job removes and when. Without it the tables only grow: every anonymous visitor who adds a
/// product leaves a guest cart, and every event and idempotency key leaves a row.
/// </summary>
public sealed class CleanupOptions
{
    public const string SectionName = "Cleanup";

    public bool Enabled { get; init; } = true;

    /// <summary>The time between two runs.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The wait before the first run, so a service that starts does not compete with its own startup and with the
    /// other instances that start at the same time.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>An active guest cart that nobody changed for this long is abandoned and deleted.</summary>
    [Range(typeof(TimeSpan), "1.00:00:00", "365.00:00:00")]
    public TimeSpan GuestCartIdleAfter { get; init; } = TimeSpan.FromDays(30);

    /// <summary>
    /// A guest cart that was merged into a customer cart is kept this long, so a client that repeats the merge
    /// request still gets its answer.
    /// </summary>
    [Range(typeof(TimeSpan), "1.00:00:00", "365.00:00:00")]
    public TimeSpan MergedCartRetention { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Published outbox messages are kept this long for support, then deleted.</summary>
    [Range(typeof(TimeSpan), "1.00:00:00", "365.00:00:00")]
    public TimeSpan ProcessedOutboxRetention { get; init; } = TimeSpan.FromDays(7);

    /// <summary>How many rows one delete statement removes, so a large backlog never holds a long lock.</summary>
    [Range(10, 10_000)]
    public int BatchSize { get; init; } = 1000;
}
