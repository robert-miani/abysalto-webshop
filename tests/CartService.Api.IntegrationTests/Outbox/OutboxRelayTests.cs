namespace CartService.Api.IntegrationTests.Outbox;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Persistence;
using CartService.Application;
using CartService.Infrastructure.Outbox;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxRelayTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;

    public OutboxRelayTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APendingMessageIsPublishedOnceAndMarkedProcessed()
    {
        await using RelayHarness harness = await RelayHarness.CreateWithNewDatabaseAsync(_postgres);
        Guid id = await harness.SeedAsync(Start, orderingKey: "checkout-42");

        int firstBatch = await harness.Relay.ProcessBatchAsync(Token);
        int secondBatch = await harness.Relay.ProcessBatchAsync(Token);

        firstBatch.ShouldBe(1);
        secondBatch.ShouldBe(0);
        ClaimedMessage published = harness.Publisher.Published.ShouldHaveSingleItem();
        published.Id.ShouldBe(id);
        published.Type.ShouldBe("CartCheckedOut");
        published.OrderingKey.ShouldBe("checkout-42");
        OutboxMessage stored = await harness.LoadAsync(id);
        stored.ProcessedAt.ShouldNotBeNull();
        stored.LockedUntil.ShouldBeNull();
        stored.Attempts.ShouldBe(1);
        stored.LastError.ShouldBeNull();
    }

    [Fact]
    public async Task PublishingAMessageIsATraceSpanWithTheMessageId()
    {
        await using RelayHarness harness = await RelayHarness.CreateWithNewDatabaseAsync(_postgres);
        Guid id = await harness.SeedAsync(Start);
        using SpanRecorder spans = new SpanRecorder(id);

        await harness.Relay.ProcessBatchAsync(Token);

        Activity span = spans.Stopped.ShouldHaveSingleItem();
        span.DisplayName.ShouldBe("CartCheckedOut publish");
        span.Kind.ShouldBe(ActivityKind.Producer);
        span.GetTagItem("messaging.system").ShouldBe("servicebus");
        span.GetTagItem("messaging.outbox.attempt").ShouldBe(1);
        span.Status.ShouldNotBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task AFailedPublishMarksTheSpanAsAnError()
    {
        await using RelayHarness harness = await RelayHarness.CreateWithNewDatabaseAsync(_postgres);
        harness.Publisher.FailWith = new InvalidOperationException("the broker is down");
        Guid id = await harness.SeedAsync(Start);
        using SpanRecorder spans = new SpanRecorder(id);

        await harness.Relay.ProcessBatchAsync(Token);

        Activity span = spans.Stopped.ShouldHaveSingleItem();
        span.Status.ShouldBe(ActivityStatusCode.Error);
        span.StatusDescription.ShouldBe("the broker is down");
    }

    [Fact]
    public async Task MessagesArePublishedOldestFirst()
    {
        await using RelayHarness harness = await RelayHarness.CreateWithNewDatabaseAsync(_postgres);
        Guid newest = await harness.SeedAsync(Start.AddMinutes(3));
        Guid oldest = await harness.SeedAsync(Start);
        Guid middle = await harness.SeedAsync(Start.AddMinutes(1));

        await harness.Relay.ProcessBatchAsync(Token);

        harness.Publisher.Published.Select(message => message.Id).ToArray().ShouldBe(new[] { oldest, middle, newest });
    }

    [Fact]
    public async Task AFailedMessageKeepsItsErrorAndIsNotRetriedBeforeTheDelayEnds()
    {
        await using RelayHarness harness = await RelayHarness.CreateWithNewDatabaseAsync(_postgres);
        harness.Publisher.FailWith = new InvalidOperationException("the broker is down");
        Guid id = await harness.SeedAsync(Start);

        int firstBatch = await harness.Relay.ProcessBatchAsync(Token);
        int immediateRetry = await harness.Relay.ProcessBatchAsync(Token);

        firstBatch.ShouldBe(1);
        immediateRetry.ShouldBe(0);
        OutboxMessage stored = await harness.LoadAsync(id);
        stored.ProcessedAt.ShouldBeNull();
        stored.Attempts.ShouldBe(1);
        stored.LastError.ShouldBe("the broker is down");
        (await harness.SecondsUntilLeaseEndsAsync(id)).ShouldBeGreaterThan(0);
        harness.Publisher.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task AFailedMessageSucceedsOnALaterAttemptAndTheErrorIsCleared()
    {
        await using RelayHarness harness = await RelayHarness.CreateWithNewDatabaseAsync(_postgres);
        harness.Publisher.FailWith = new InvalidOperationException("the broker is down");
        Guid id = await harness.SeedAsync(Start);
        await harness.Relay.ProcessBatchAsync(Token);

        harness.Publisher.FailWith = null;
        await harness.ExpireLeasesAsync();
        int retry = await harness.Relay.ProcessBatchAsync(Token);

        retry.ShouldBe(1);
        harness.Publisher.Published.ShouldHaveSingleItem().Id.ShouldBe(id);
        OutboxMessage stored = await harness.LoadAsync(id);
        stored.ProcessedAt.ShouldNotBeNull();
        stored.Attempts.ShouldBe(2);
        stored.LastError.ShouldBeNull();
    }

    [Fact]
    public async Task TheRetryDelayGrowsWithEveryAttempt()
    {
        OutboxOptions options = new OutboxOptions { RetryDelay = TimeSpan.FromMinutes(1) };
        await using RelayHarness harness = await RelayHarness.CreateWithNewDatabaseAsync(_postgres, options);
        harness.Publisher.FailWith = new InvalidOperationException("the broker is down");
        Guid id = await harness.SeedAsync(Start);

        await harness.Relay.ProcessBatchAsync(Token);
        double afterFirst = await harness.SecondsUntilLeaseEndsAsync(id);
        await harness.ExpireLeasesAsync();
        await harness.Relay.ProcessBatchAsync(Token);
        double afterSecond = await harness.SecondsUntilLeaseEndsAsync(id);

        afterFirst.ShouldBeInRange(45, 60);
        afterSecond.ShouldBeInRange(105, 120);
    }

    [Fact]
    public async Task AMessageThatFailedTooOftenIsLeftInTheTableAndNotClaimedAgain()
    {
        OutboxOptions options = new OutboxOptions { MaxAttempts = 3 };
        await using RelayHarness harness = await RelayHarness.CreateWithNewDatabaseAsync(_postgres, options);
        harness.Publisher.FailWith = new InvalidOperationException("poison message");
        Guid id = await harness.SeedAsync(Start);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            (await harness.Relay.ProcessBatchAsync(Token)).ShouldBe(1);
            await harness.ExpireLeasesAsync();
        }

        int afterTheLimit = await harness.Relay.ProcessBatchAsync(Token);

        afterTheLimit.ShouldBe(0);
        OutboxMessage stored = await harness.LoadAsync(id);
        stored.ProcessedAt.ShouldBeNull();
        stored.Attempts.ShouldBe(3);
        stored.LastError.ShouldBe("poison message");
    }

    [Fact]
    public async Task AVeryLongErrorTextIsTruncatedToFitTheColumn()
    {
        await using RelayHarness harness = await RelayHarness.CreateWithNewDatabaseAsync(_postgres);
        harness.Publisher.FailWith = new InvalidOperationException(new string('x', 5000));
        Guid id = await harness.SeedAsync(Start);

        await harness.Relay.ProcessBatchAsync(Token);

        (await harness.LoadAsync(id)).LastError!.Length.ShouldBe(OutboxMessageConfiguration.MaxErrorLength);
    }

    [Fact]
    public async Task AMessageOfARelayThatDiedIsTakenOverWhenItsLeaseHasExpired()
    {
        await using RelayHarness harness = await RelayHarness.CreateWithNewDatabaseAsync(_postgres);
        Guid id = await harness.SeedAsync(Start);

        // A relay claims the message and dies before it publishes: simulate it with a claim and nothing else.
        await ClaimWithoutPublishingAsync(harness);
        int whileLeased = await harness.Relay.ProcessBatchAsync(Token);
        await harness.ExpireLeasesAsync();
        int afterExpiry = await harness.Relay.ProcessBatchAsync(Token);

        whileLeased.ShouldBe(0);
        afterExpiry.ShouldBe(1);
        harness.Publisher.Published.ShouldHaveSingleItem().Id.ShouldBe(id);
        (await harness.LoadAsync(id)).Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task TwoRelaysWorkingAtTheSameTimeNeverPublishTheSameMessageTwice()
    {
        OutboxOptions options = new OutboxOptions { BatchSize = 5 };
        string connectionString = await _postgres.CreateIsolatedDatabaseAsync($"relay_{Guid.NewGuid():N}");
        await using RelayHarness first = RelayHarness.Create(connectionString, options);
        await using RelayHarness second = RelayHarness.Create(connectionString, options);
        List<Guid> seeded = new List<Guid>();

        for (int index = 0; index < 40; index++)
        {
            seeded.Add(await first.SeedAsync(Start.AddSeconds(index), orderingKey: $"checkout-{index}"));
        }

        await Task.WhenAll(DrainAsync(first), DrainAsync(second));

        List<Guid> published = first.Publisher.Published.Concat(second.Publisher.Published).Select(message => message.Id).ToList();
        published.Count.ShouldBe(40);
        published.Distinct().Count().ShouldBe(40);
        published.Order().ShouldBe(seeded.Order());
        first.Publisher.Published.Count.ShouldBeGreaterThan(0);
        second.Publisher.Published.Count.ShouldBeGreaterThan(0);
    }

    private static async Task DrainAsync(RelayHarness harness)
    {
        while (await harness.Relay.ProcessBatchAsync(Token) > 0)
        {
        }
    }

    private static async Task ClaimWithoutPublishingAsync(RelayHarness harness)
    {
        await using CartService.Infrastructure.Persistence.CartDbContext context = PostgresFixture.CreateContext(harness.ConnectionString);
        OutboxStore store = new OutboxStore(context);

        IReadOnlyList<ClaimedMessage> claimed = await store.ClaimBatchAsync(10, TimeSpan.FromSeconds(30), 10, Token);
        claimed.ShouldHaveSingleItem();
    }
}

/// <summary>
/// Collects the finished spans of one outbox message. Spans of other messages, which tests running in parallel
/// make, are ignored.
/// </summary>
internal sealed class SpanRecorder : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<Activity> _stopped = new ConcurrentQueue<Activity>();

    public SpanRecorder(Guid messageId)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CartTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (Equals(activity.GetTagItem("messaging.message.id"), messageId))
                {
                    _stopped.Enqueue(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyCollection<Activity> Stopped => _stopped;

    public void Dispose()
    {
        _listener.Dispose();
    }
}
