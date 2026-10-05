namespace CartService.Infrastructure.Caching;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// How the service uses Redis as a cache for carts. Without a connection string there is no cache and every read
/// goes to the database.
/// </summary>
public sealed class CartCacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>The Redis connection string, for example <c>redis:6379</c>. Empty means no cache.</summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// How long a cart stays in the cache. Every change removes the cart, so this only bounds how long an old copy
    /// can survive when removing it was not possible, for example while Redis was unreachable.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan EntryLifetime { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long one cache call may take. A slow cache is worse than no cache, so the call is given up and the
    /// database answers instead.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.050", "00:00:10")]
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// After too many failed calls the cache is skipped for this long, so a Redis outage does not add a delay to
    /// every request.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(15);
}
