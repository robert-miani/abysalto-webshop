namespace CartService.Infrastructure.Outbox;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Publishes the events in the outbox to the message broker, in the background. The delivery is at least once:
/// if the relay stops after it published a message but before it marked the message as processed, the message is
/// published again, which consumers handle by skipping the event ids they have seen.
/// </summary>
internal sealed class OutboxRelay : BackgroundService
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutboxPublisher _publisher;
    private readonly IOptions<OutboxOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<OutboxRelay> _logger;

    public OutboxRelay(
        IServiceScopeFactory scopeFactory,
        IOutboxPublisher publisher,
        IOptions<OutboxOptions> options,
        TimeProvider time,
        ILogger<OutboxRelay> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Claims one batch and publishes it. Returns the number of messages that were claimed.
    /// </summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        OutboxOptions options = _options.Value;

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        OutboxStore store = scope.ServiceProvider.GetRequiredService<OutboxStore>();

        IReadOnlyList<ClaimedMessage> batch = await store.ClaimBatchAsync(
            options.BatchSize,
            options.LeaseDuration,
            options.MaxAttempts,
            cancellationToken);

        foreach (ClaimedMessage message in batch)
        {
            // The span joins the trace of the request that wrote the event, if the event carries one.
            ActivityContext parent = OutboxTraceContext.TryRead(message.Payload, out ActivityContext carried) ? carried : default;
            using Activity? activity = CartTelemetry.ActivitySource.StartActivity($"{message.Type} publish", ActivityKind.Producer, parent);
            activity?.SetTag("messaging.system", "servicebus");
            activity?.SetTag("messaging.message.id", message.Id);
            activity?.SetTag("messaging.outbox.attempt", message.Attempt);
            long started = Stopwatch.GetTimestamp();

            try
            {
                await _publisher.PublishAsync(message, cancellationToken);
                await store.MarkProcessedAsync(message.Id, cancellationToken);
                CartTelemetry.OutboxPublished.Add(1, CartTelemetry.Tag("result", "success"));
                _logger.LogInformation("Published {EventType} {MessageId} on attempt {Attempt}.", message.Type, message.Id, message.Attempt);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                CartTelemetry.OutboxPublished.Add(1, CartTelemetry.Tag("result", "failure"));
                await HandleFailureAsync(store, message, exception, options, cancellationToken);
            }
            finally
            {
                CartTelemetry.OutboxPublishDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
        }

        return batch.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        OutboxOptions options = _options.Value;

        if (!options.Enabled)
        {
            _logger.LogInformation("The outbox relay is disabled.");
            return;
        }

        _logger.LogInformation("The outbox relay started and polls every {PollInterval}.", options.PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            int claimed = 0;

            try
            {
                claimed = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // For example the database is not reachable. Nothing is lost: the messages stay in the table.
                _logger.LogError(exception, "The outbox relay could not process a batch and will try again.");
            }

            if (claimed == 0)
            {
                try
                {
                    await Task.Delay(options.PollInterval, _time, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleFailureAsync(
        OutboxStore store,
        ClaimedMessage message,
        Exception exception,
        OutboxOptions options,
        CancellationToken cancellationToken)
    {
        TimeSpan delay = TimeSpan.FromTicks(Math.Min(options.RetryDelay.Ticks * message.Attempt, MaxRetryDelay.Ticks));
        await store.MarkFailedAsync(message.Id, exception.Message, delay, cancellationToken);

        if (message.Attempt >= options.MaxAttempts)
        {
            _logger.LogError(
                exception,
                "Giving up on {EventType} {MessageId} after {Attempt} attempts. It stays in the outbox and needs attention.",
                message.Type,
                message.Id,
                message.Attempt);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Could not publish {EventType} {MessageId} on attempt {Attempt}. The next attempt is in {Delay}.",
                message.Type,
                message.Id,
                message.Attempt,
                delay);
        }
    }
}
