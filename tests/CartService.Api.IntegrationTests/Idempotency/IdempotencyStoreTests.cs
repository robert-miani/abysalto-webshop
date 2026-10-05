namespace CartService.Api.IntegrationTests.Idempotency;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Persistence;
using CartService.Application.Abstractions;
using CartService.Infrastructure.Idempotency;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IdempotencyStoreTests
{
    private readonly PostgresFixture _postgres;
    private readonly string _scope = $"customer:{Guid.NewGuid()}";

    public IdempotencyStoreTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANewKeyStartsTheRequest()
    {
        IdempotencyClaim claim = await BeginAsync("key-1", "fingerprint-a");

        claim.Outcome.ShouldBe(IdempotencyOutcome.Started);
    }

    [Fact]
    public async Task TheSameRequestWhileTheFirstIsStillRunningIsInProgress()
    {
        await BeginAsync("key-1", "fingerprint-a");

        IdempotencyClaim second = await BeginAsync("key-1", "fingerprint-a");

        second.Outcome.ShouldBe(IdempotencyOutcome.InProgress);
    }

    [Fact]
    public async Task TheSameKeyForADifferentRequestIsReported()
    {
        await BeginAsync("key-1", "fingerprint-a");

        IdempotencyClaim second = await BeginAsync("key-1", "fingerprint-b");

        second.Outcome.ShouldBe(IdempotencyOutcome.KeyReused);
    }

    [Fact]
    public async Task AfterTheRequestSucceededItsResponseIsReturnedAgain()
    {
        await BeginAsync("key-1", "fingerprint-a");
        await CompleteAsync("key-1", 200, "{\"cart\":1}");

        IdempotencyClaim repeated = await BeginAsync("key-1", "fingerprint-a");

        repeated.Outcome.ShouldBe(IdempotencyOutcome.Completed);
        repeated.ResponseStatusCode.ShouldBe(200);
        repeated.ResponseBody.ShouldBe("{\"cart\":1}");
    }

    [Fact]
    public async Task AResponseWithoutABodyIsReturnedAgainWithoutABody()
    {
        await BeginAsync("key-1", "fingerprint-a");
        await CompleteAsync("key-1", 204, null);

        IdempotencyClaim repeated = await BeginAsync("key-1", "fingerprint-a");

        repeated.Outcome.ShouldBe(IdempotencyOutcome.Completed);
        repeated.ResponseStatusCode.ShouldBe(204);
        repeated.ResponseBody.ShouldBeNull();
    }

    [Fact]
    public async Task AfterTheRequestSucceededAnotherRequestWithTheSameKeyIsStillReported()
    {
        await BeginAsync("key-1", "fingerprint-a");
        await CompleteAsync("key-1", 200, "{}");

        IdempotencyClaim other = await BeginAsync("key-1", "fingerprint-b");

        other.Outcome.ShouldBe(IdempotencyOutcome.KeyReused);
    }

    [Fact]
    public async Task AReleasedKeyCanBeUsedAgainEvenForADifferentRequest()
    {
        await BeginAsync("key-1", "fingerprint-a");
        await ReleaseAsync("key-1");

        IdempotencyClaim again = await BeginAsync("key-1", "fingerprint-b");

        again.Outcome.ShouldBe(IdempotencyOutcome.Started);
    }

    [Fact]
    public async Task CompletingAReleasedKeyStoresNothing()
    {
        await BeginAsync("key-1", "fingerprint-a");
        await ReleaseAsync("key-1");
        await CompleteAsync("key-1", 200, "{}");

        IdempotencyClaim again = await BeginAsync("key-1", "fingerprint-a");

        again.Outcome.ShouldBe(IdempotencyOutcome.Started);
    }

    [Fact]
    public async Task TheSameKeyOfTwoRequestersDoesNotInterfere()
    {
        string otherScope = $"guest:{Guid.NewGuid()}";
        await BeginAsync("key-1", "fingerprint-a");

        IdempotencyClaim other = await BeginAsync(otherScope, "key-1", "fingerprint-b");

        other.Outcome.ShouldBe(IdempotencyOutcome.Started);
    }

    [Fact]
    public async Task ARunningRequestThatDiedFreesItsKeyWhenTheLeaseHasExpired()
    {
        await BeginAsync("key-1", "fingerprint-a");
        await ExpireRecordsAsync();

        IdempotencyClaim again = await BeginAsync("key-1", "fingerprint-b");

        again.Outcome.ShouldBe(IdempotencyOutcome.Started);
    }

    [Fact]
    public async Task AStoredResponseIsForgottenWhenItsRetentionHasExpired()
    {
        await BeginAsync("key-1", "fingerprint-a");
        await CompleteAsync("key-1", 200, "{}");
        await ExpireRecordsAsync();

        IdempotencyClaim again = await BeginAsync("key-1", "fingerprint-a");

        again.Outcome.ShouldBe(IdempotencyOutcome.Started);
    }

    [Fact]
    public async Task OfTwentyRequestsWithTheSameKeyAtTheSameTimeExactlyOneStarts()
    {
        IdempotencyClaim[] claims = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => BeginAsync("key-race", "fingerprint-a")));

        claims.Count(claim => claim.Outcome == IdempotencyOutcome.Started).ShouldBe(1);
        claims.Count(claim => claim.Outcome == IdempotencyOutcome.InProgress).ShouldBe(19);
    }

    private Task<IdempotencyClaim> BeginAsync(string key, string fingerprint)
    {
        return BeginAsync(_scope, key, fingerprint);
    }

    private async Task<IdempotencyClaim> BeginAsync(string scope, string key, string fingerprint)
    {
        await using CartDbContext context = _postgres.CreateContext();

        return await CreateStore(context).BeginAsync(scope, key, fingerprint, Token);
    }

    private async Task CompleteAsync(string key, int statusCode, string? body)
    {
        await using CartDbContext context = _postgres.CreateContext();
        await CreateStore(context).CompleteAsync(_scope, key, statusCode, body, Token);
    }

    private async Task ReleaseAsync(string key)
    {
        await using CartDbContext context = _postgres.CreateContext();
        await CreateStore(context).ReleaseAsync(_scope, key, Token);
    }

    private async Task ExpireRecordsAsync()
    {
        await using CartDbContext context = _postgres.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE idempotency_records SET expires_at = now() - interval '1 second' WHERE scope = {_scope}",
            Token);
    }

    private static IdempotencyStore CreateStore(CartDbContext context)
    {
        return new IdempotencyStore(context, Options.Create(new IdempotencyOptions()));
    }
}
