namespace CartService.Api.RateLimiting;

using System;

/// <summary>
/// How many requests a client may send. The limit protects the service from one client that loops or abuses it;
/// the edge of the platform (Front Door and API Management) protects against floods.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Turns the limits off. Tests and local load tests do that.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The limit for every cart request: 10 per second on average, in bursts of 60.</summary>
    public RateLimitBucket Default { get; init; } = new RateLimitBucket
    {
        TokenLimit = 60,
        TokensPerPeriod = 10,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
    };

    /// <summary>
    /// The limit for requests that are expensive or attractive to abuse: checkout, and creating a cart, which an
    /// anonymous visitor can do without any proof. It allows 10 per minute on average, in bursts of 10.
    /// </summary>
    public RateLimitBucket Strict { get; init; } = new RateLimitBucket
    {
        TokenLimit = 10,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(6),
    };
}
