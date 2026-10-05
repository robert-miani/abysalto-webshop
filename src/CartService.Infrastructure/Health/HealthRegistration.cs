namespace CartService.Infrastructure.Health;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

internal static class HealthRegistration
{
    public static IServiceCollection AddCartHealthChecks(this IServiceCollection services)
    {
        string[] ready = { HealthTags.Ready };

        // Only the database makes the service unready. Redis and the delivery of events make it degraded, which
        // still receives traffic.
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("postgres", HealthStatus.Unhealthy, ready)
            .AddCheck<CacheHealthCheck>("redis", HealthStatus.Degraded, ready)
            .AddCheck<OutboxHealthCheck>("outbox", HealthStatus.Degraded, ready);

        return services;
    }
}
