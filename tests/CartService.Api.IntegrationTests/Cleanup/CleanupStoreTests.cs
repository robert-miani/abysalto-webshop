namespace CartService.Api.IntegrationTests.Cleanup;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Persistence;
using CartService.Domain;
using CartService.Infrastructure.Cleanup;
using CartService.Infrastructure.Idempotency;
using CartService.Infrastructure.Outbox;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CleanupStoreTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;

    public CleanupStoreTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AGuestCartThatWasIdleForTooLongIsDeletedWithItsItems()
    {
        string database = await CreateDatabaseAsync();
        Guid abandoned = await AddGuestCartAsync(database, Now.AddDays(-31), withItem: true);
        Guid recent = await AddGuestCartAsync(database, Now.AddDays(-29), withItem: true);

        CleanupResult result = await RunAsync(database);

        result.Carts.ShouldBe(1);
        (await CartIdsAsync(database)).ShouldBe(new[] { recent });
        (await CountItemsAsync(database, abandoned)).ShouldBe(0);
        (await CountItemsAsync(database, recent)).ShouldBe(1);
    }

    [Fact]
    public async Task CustomerCartsAreNeverDeletedHoweverOldTheyAre()
    {
        string database = await CreateDatabaseAsync();
        Guid idle = await AddCustomerCartAsync(database, Now.AddDays(-400));
        Guid pending = await AddCustomerCartAsync(database, Now.AddDays(-400), checkOut: true);

        CleanupResult result = await RunAsync(database);

        result.Carts.ShouldBe(0);
        (await CartIdsAsync(database)).OrderBy(id => id).ShouldBe(new[] { idle, pending }.OrderBy(id => id));
    }

    [Fact]
    public async Task AMergedGuestCartIsKeptForTheRetentionAndThenDeleted()
    {
        string database = await CreateDatabaseAsync();
        Guid oldMerged = await AddMergedGuestCartAsync(database, Now.AddDays(-8));
        Guid freshMerged = await AddMergedGuestCartAsync(database, Now.AddDays(-6));

        CleanupResult result = await RunAsync(database);

        result.Carts.ShouldBe(1);
        Guid[] remaining = await CartIdsAsync(database);
        remaining.ShouldContain(freshMerged);
        remaining.ShouldNotContain(oldMerged);
    }

    [Fact]
    public async Task OnlyPublishedOutboxMessagesThatAreOldEnoughAreDeleted()
    {
        string database = await CreateDatabaseAsync();
        Guid oldProcessed = await AddOutboxMessageAsync(database, processedAt: Now.AddDays(-8));
        Guid recentProcessed = await AddOutboxMessageAsync(database, processedAt: Now.AddDays(-1));
        Guid oldPending = await AddOutboxMessageAsync(database, processedAt: null);

        CleanupResult result = await RunAsync(database);

        result.OutboxMessages.ShouldBe(1);
        Guid[] remaining = await OutboxIdsAsync(database);
        remaining.ShouldContain(recentProcessed);
        remaining.ShouldContain(oldPending);
        remaining.ShouldNotContain(oldProcessed);
    }

    [Fact]
    public async Task ExpiredIdempotencyRecordsAreDeleted()
    {
        string database = await CreateDatabaseAsync();
        await AddIdempotencyRecordsAsync(database, expired: 3, valid: 2);

        CleanupResult result = await RunAsync(database);

        result.IdempotencyRecords.ShouldBe(3);
        (await CountIdempotencyRecordsAsync(database)).ShouldBe(2);
    }

    [Fact]
    public async Task ABacklogLargerThanABatchIsDeletedInSeveralBatches()
    {
        string database = await CreateDatabaseAsync();
        await AddIdempotencyRecordsAsync(database, expired: 35, valid: 1);

        CleanupResult result = await RunAsync(database, batchSize: 10);

        result.IdempotencyRecords.ShouldBe(35);
        (await CountIdempotencyRecordsAsync(database)).ShouldBe(1);
    }

    [Fact]
    public async Task WhenThereIsNothingToDeleteNothingIsDeleted()
    {
        string database = await CreateDatabaseAsync();
        await AddGuestCartAsync(database, Now.AddDays(-1), withItem: false);
        await AddOutboxMessageAsync(database, processedAt: null);

        CleanupResult result = await RunAsync(database);

        result.Total.ShouldBe(0);
    }

    [Fact]
    public async Task RunningTwiceDeletesNothingTheSecondTime()
    {
        string database = await CreateDatabaseAsync();
        await AddGuestCartAsync(database, Now.AddDays(-40), withItem: true);

        CleanupResult first = await RunAsync(database);
        CleanupResult second = await RunAsync(database);

        first.Carts.ShouldBe(1);
        second.Total.ShouldBe(0);
    }

    private async Task<string> CreateDatabaseAsync()
    {
        return await _postgres.CreateIsolatedDatabaseAsync($"cleanup_{Guid.NewGuid():N}");
    }

    private static async Task<CleanupResult> RunAsync(string database, int batchSize = 1000)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(database);
        CleanupStore store = new CleanupStore(context, Options.Create(new CleanupOptions { BatchSize = batchSize }));

        return await store.RunAsync(Now, Token);
    }

    private static async Task<Guid> AddGuestCartAsync(string database, DateTimeOffset updatedAt, bool withItem)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(database);
        Cart cart = Cart.CreateForGuest(Guid.NewGuid().ToString("N"), updatedAt);

        if (withItem)
        {
            cart.AddItem("cap-red", "Red cap", Money.Eur(9.50m), 1, updatedAt);
        }

        context.Carts.Add(cart);
        await context.SaveChangesAsync(Token);

        return cart.Id;
    }

    private static async Task<Guid> AddCustomerCartAsync(string database, DateTimeOffset updatedAt, bool checkOut = false)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(database);
        Cart cart = Cart.CreateForCustomer(Guid.NewGuid(), updatedAt);

        if (checkOut)
        {
            cart.AddItem("cap-red", "Red cap", Money.Eur(9.50m), 1, updatedAt);
            cart.Checkout(updatedAt);
        }

        context.Carts.Add(cart);
        await context.SaveChangesAsync(Token);

        return cart.Id;
    }

    private static async Task<Guid> AddMergedGuestCartAsync(string database, DateTimeOffset mergedAt)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(database);
        Cart guest = Cart.CreateForGuest(Guid.NewGuid().ToString("N"), mergedAt);
        guest.AddItem("cap-red", "Red cap", Money.Eur(9.50m), 1, mergedAt);
        Cart customer = Cart.CreateForCustomer(Guid.NewGuid(), mergedAt);
        customer.MergeGuestCart(guest, mergedAt);
        context.Carts.AddRange(guest, customer);
        await context.SaveChangesAsync(Token);

        return guest.Id;
    }

    private static async Task<Guid> AddOutboxMessageAsync(string database, DateTimeOffset? processedAt)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(database);
        OutboxMessage message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "CartCheckedOut",
            OrderingKey = "key",
            Payload = "{}",
            OccurredAt = Now.AddDays(-30),
            ProcessedAt = processedAt,
        };
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync(Token);

        return message.Id;
    }

    private static async Task AddIdempotencyRecordsAsync(string database, int expired, int valid)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(database);

        for (int index = 0; index < expired + valid; index++)
        {
            bool isExpired = index < expired;
            context.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Scope = "customer:test",
                Key = $"key-{index}",
                Fingerprint = "fingerprint",
                Status = IdempotencyRecord.Completed,
                ResponseStatusCode = 200,
                CreatedAt = Now.AddDays(-2),
                ExpiresAt = isExpired ? Now.AddHours(-1) : Now.AddHours(1),
            });
        }

        await context.SaveChangesAsync(Token);
    }

    private static async Task<Guid[]> CartIdsAsync(string database)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(database);

        return await context.Carts.AsNoTracking().Select(cart => cart.Id).ToArrayAsync(Token);
    }

    private static async Task<Guid[]> OutboxIdsAsync(string database)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(database);

        return await context.OutboxMessages.AsNoTracking().Select(message => message.Id).ToArrayAsync(Token);
    }

    private static async Task<long> CountItemsAsync(string database, Guid cartId)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(database);

        return await context.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM cart_items WHERE cart_id = {cartId}").SingleAsync(Token);
    }

    private static async Task<int> CountIdempotencyRecordsAsync(string database)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(database);

        return await context.IdempotencyRecords.CountAsync(Token);
    }
}
