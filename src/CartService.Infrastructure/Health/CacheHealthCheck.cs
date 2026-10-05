namespace CartService.Infrastructure.Health;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Infrastructure.Caching;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// The service works without Redis, only slower, so a failure here makes it degraded and not unready. A service
/// that is taken out of rotation because the cache is down would turn a slowdown into an outage.
/// </summary>
internal sealed class CacheHealthCheck : IHealthCheck
{
    private readonly ICartCache _cache;

    public CacheHealthCheck(ICartCache cache)
    {
        _cache = cache;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_cache is not RedisCartCache redis)
        {
            return HealthCheckResult.Healthy("No cache is configured, so carts are read from the database.");
        }

        try
        {
            await redis.PingAsync(cancellationToken);

            return HealthCheckResult.Healthy("The cache answers.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "The cache does not answer, so carts are read from the database.", exception);
        }
    }
}
