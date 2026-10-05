namespace CartService.Infrastructure.Cleanup;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Runs the cleanup in the background, once per interval. A failed run is logged and the next one tries again;
/// nothing else in the service depends on it.
/// </summary>
internal sealed class CleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<CleanupOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(IServiceScopeFactory scopeFactory, IOptions<CleanupOptions> options, TimeProvider time, ILogger<CleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>Runs the cleanup once and returns what it deleted.</summary>
    public async Task<CleanupResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        // A span around the run, so its database calls are traced as one unit of work.
        using Activity? activity = CartTelemetry.ActivitySource.StartActivity("cleanup", ActivityKind.Internal);
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        CleanupStore store = scope.ServiceProvider.GetRequiredService<CleanupStore>();

        CleanupResult result = await store.RunAsync(_time.GetUtcNow(), cancellationToken);

        activity?.SetTag("cleanup.deleted.carts", result.Carts);
        activity?.SetTag("cleanup.deleted.outbox_messages", result.OutboxMessages);
        activity?.SetTag("cleanup.deleted.idempotency_records", result.IdempotencyRecords);
        CartTelemetry.CleanupDeleted.Add(result.Carts, CartTelemetry.Tag("kind", "carts"));
        CartTelemetry.CleanupDeleted.Add(result.OutboxMessages, CartTelemetry.Tag("kind", "outbox_messages"));
        CartTelemetry.CleanupDeleted.Add(result.IdempotencyRecords, CartTelemetry.Tag("kind", "idempotency_records"));

        if (result.Total > 0)
        {
            _logger.LogInformation(
                "Cleanup deleted {Carts} carts, {OutboxMessages} outbox messages and {IdempotencyRecords} idempotency records.",
                result.Carts,
                result.OutboxMessages,
                result.IdempotencyRecords);
        }

        return result;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CleanupOptions options = _options.Value;

        if (!options.Enabled)
        {
            _logger.LogInformation("The cleanup job is disabled.");

            return;
        }

        try
        {
            await Task.Delay(options.InitialDelay, _time, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "The cleanup failed and will run again in {Interval}.", options.Interval);
                }

                await Task.Delay(options.Interval, _time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The service is stopping.
        }
    }
}
