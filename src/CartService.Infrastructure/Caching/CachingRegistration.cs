namespace CartService.Infrastructure.Caching;

using CartService.Application.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

internal static class CachingRegistration
{
    public static IServiceCollection AddCartCache(this IServiceCollection services)
    {
        services.AddOptions<CartCacheOptions>()
            .BindConfiguration(CartCacheOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The Redis client connects when it is first used, not when the service starts, and a Redis that is not
        // there never stops the service from starting.
        services.AddStackExchangeRedisCache(_ => { });
        services.AddOptions<RedisCacheOptions>()
            .Configure<IOptions<CartCacheOptions>>((redis, cache) => redis.ConfigurationOptions = CreateConfiguration(cache.Value));

        // Whether there is a cache is decided on first use, so the settings of a test or a deployment apply.
        services.AddSingleton<ICartCache>(serviceProvider =>
        {
            IOptions<CartCacheOptions> options = serviceProvider.GetRequiredService<IOptions<CartCacheOptions>>();

            return string.IsNullOrWhiteSpace(options.Value.ConnectionString)
                ? new NullCartCache()
                : new RedisCartCache(
                    serviceProvider.GetRequiredService<IDistributedCache>(),
                    options,
                    serviceProvider.GetRequiredService<ILogger<RedisCartCache>>());
        });

        return services;
    }

    private static ConfigurationOptions? CreateConfiguration(CartCacheOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return null;
        }

        int timeout = (int)options.Timeout.TotalMilliseconds;
        ConfigurationOptions configuration = ConfigurationOptions.Parse(options.ConnectionString);

        // The service starts and works without Redis, and no call waits longer than the timeout.
        configuration.AbortOnConnectFail = false;
        configuration.ConnectRetry = 1;
        configuration.ConnectTimeout = timeout;
        configuration.SyncTimeout = timeout;
        configuration.AsyncTimeout = timeout;

        return configuration;
    }
}
