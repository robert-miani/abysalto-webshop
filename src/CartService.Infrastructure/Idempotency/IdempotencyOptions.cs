namespace CartService.Infrastructure.Idempotency;

using System;
using System.ComponentModel.DataAnnotations;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    /// <summary>
    /// How long a running request owns its key. If the service dies in the middle of a request, the key is free
    /// again after this time.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan InProgressLease { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the response of a successful request is kept for a repeated request.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
    public TimeSpan Retention { get; init; } = TimeSpan.FromHours(24);
}
