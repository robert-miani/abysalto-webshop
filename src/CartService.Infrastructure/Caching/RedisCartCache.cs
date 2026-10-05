namespace CartService.Infrastructure.Caching;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using CartService.Domain;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;

/// <summary>
/// Keeps carts in Redis. Each call has a short timeout and a circuit breaker, and every failure is the same as a
/// cache that has nothing: the cart is read from the database. The cache must never make a request fail or wait.
/// </summary>
/// <remarks>
/// A cart is stored twice, under its id and, when it is the active cart of a customer, under the customer. The key
/// has a version, so a release that changes the shape of an entry never reads the entries of the old release.
/// </remarks>
internal sealed class RedisCartCache : ICartCache
{
    private const string KeyPrefix = "cart:v1:";
    private const string HealthKey = KeyPrefix + "health";

    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    private readonly IDistributedCache _cache;
    private readonly IOptions<CartCacheOptions> _options;
    private readonly ILogger<RedisCartCache> _logger;
    private readonly ResiliencePipeline _pipeline;

    public RedisCartCache(IDistributedCache cache, IOptions<CartCacheOptions> options, ILogger<RedisCartCache> logger)
    {
        _cache = cache;
        _options = options;
        _logger = logger;
        _pipeline = CreatePipeline(options.Value, logger);
    }

    public Task<CachedCart?> GetByIdAsync(Guid cartId, CancellationToken cancellationToken)
    {
        return ReadAsync(IdKey(cartId), cancellationToken);
    }

    public Task<CachedCart?> GetActiveByCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return ReadAsync(CustomerKey(customerId), cancellationToken);
    }

    public async Task SetAsync(Cart cart, CartDto dto, CancellationToken cancellationToken)
    {
        byte[] entry = JsonSerializer.SerializeToUtf8Bytes(CachedCart.From(cart, dto), Json);
        DistributedCacheEntryOptions lifetime = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.Value.EntryLifetime,
        };
        bool isActiveCartOfCustomer = cart.CustomerId.HasValue && cart.Status == CartStatus.Active;

        await RunAsync(
            async token =>
            {
                await _cache.SetAsync(IdKey(cart.Id), entry, lifetime, token);

                if (isActiveCartOfCustomer)
                {
                    await _cache.SetAsync(CustomerKey(cart.CustomerId!.Value), entry, lifetime, token);
                }
            },
            "store a cart",
            cancellationToken);
    }

    public async Task RemoveAsync(Cart cart)
    {
        await RunAsync(
            async token =>
            {
                await _cache.RemoveAsync(IdKey(cart.Id), token);

                if (cart.CustomerId.HasValue)
                {
                    await _cache.RemoveAsync(CustomerKey(cart.CustomerId.Value), token);
                }
            },
            "remove a cart",
            CancellationToken.None);
    }

    /// <summary>
    /// Asks Redis for a key that does not exist. Unlike the cache methods this throws when Redis cannot answer,
    /// because the readiness check needs to know.
    /// </summary>
    public async Task PingAsync(CancellationToken cancellationToken)
    {
        await _pipeline.ExecuteAsync(async token => await WithHardTimeout(_cache.GetAsync(HealthKey, token), token), cancellationToken);
    }

    // The timeout of the pipeline only cancels a call that listens to its token, and the Redis client does not
    // always listen while it tries to connect. This bound holds either way.
    private Task WithHardTimeout(Task operation, CancellationToken cancellationToken)
    {
        return operation.WaitAsync(_options.Value.Timeout, cancellationToken);
    }

    private static string IdKey(Guid cartId)
    {
        return $"{KeyPrefix}id:{cartId:N}";
    }

    private static string CustomerKey(Guid customerId)
    {
        return $"{KeyPrefix}customer:{customerId:N}";
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    private static ResiliencePipeline CreatePipeline(CartCacheOptions options, ILogger logger)
    {
        // The circuit breaker is the outer strategy, so a call that timed out counts as a failure.
        return new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = options.BreakDuration,
                OnOpened = _ =>
                {
                    logger.LogWarning("The cart cache failed repeatedly and is skipped for {BreakDuration}; carts are read from the database", options.BreakDuration);

                    return default;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("The cart cache works again");

                    return default;
                },
            })
            .AddTimeout(options.Timeout)
            .Build();
    }

    private async Task<CachedCart?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        byte[]? entry = null;
        bool answered = await RunAsync(async token => entry = await _cache.GetAsync(key, token), "read a cart", cancellationToken);

        if (!answered || entry is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CachedCart>(entry, Json);
        }
        catch (JsonException exception)
        {
            // An entry that cannot be read is no better than no entry. The database answers and the next read
            // stores a good one.
            _logger.LogWarning(exception, "A cached cart could not be read and is ignored");

            return null;
        }
    }

    // Returns false when the cache did not answer. The caller's own cancellation is not a failure of the cache.
    private async Task<bool> RunAsync(Func<CancellationToken, Task> operation, string description, CancellationToken cancellationToken)
    {
        try
        {
            await _pipeline.ExecuteAsync(async token => await WithHardTimeout(operation(token), token), cancellationToken);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokenCircuitException)
        {
            // Already reported when the circuit opened, and it would repeat for every request.
            _logger.LogDebug("The cart cache is skipped, so the service did not {Operation}", description);

            return false;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The cart cache could not {Operation}", description);

            return false;
        }
    }
}
