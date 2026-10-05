namespace CartService.Api.RateLimiting;

using System;

/// <summary>
/// A token bucket: every request takes one token, and the bucket is refilled over time. A client can send a burst
/// as large as <see cref="TokenLimit"/> and then, on average, <see cref="TokensPerPeriod"/> per
/// <see cref="ReplenishmentPeriod"/>.
/// </summary>
public sealed class RateLimitBucket
{
    /// <summary>The size of the bucket, which is the largest burst.</summary>
    public int TokenLimit { get; init; }

    /// <summary>How many tokens are added every <see cref="ReplenishmentPeriod"/>.</summary>
    public int TokensPerPeriod { get; init; }

    public TimeSpan ReplenishmentPeriod { get; init; }
}
