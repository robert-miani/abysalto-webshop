namespace CartService.Api.IntegrationTests.Outbox;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Persistence;
using CartService.Infrastructure.Outbox;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// A relay wired to its own database and a fake publisher, so a test controls the outbox table completely.
/// </summary>
internal sealed class RelayHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private RelayHarness(string connectionString, ServiceProvider provider, OutboxRelay relay, FakeOutboxPublisher publisher)
    {
        ConnectionString = connectionString;
        _provider = provider;
        Relay = relay;
        Publisher = publisher;
    }

    public string ConnectionString { get; }

    public OutboxRelay Relay { get; }

    public FakeOutboxPublisher Publisher { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static RelayHarness Create(string connectionString, OutboxOptions options, FakeOutboxPublisher? publisher = null)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<CartDbContext>(builder => builder.UseCartDatabase(connectionString));
        services.AddScoped<OutboxStore>();
        ServiceProvider provider = services.BuildServiceProvider();

        FakeOutboxPublisher fake = publisher ?? new FakeOutboxPublisher();
        OutboxRelay relay = new OutboxRelay(
            provider.GetRequiredService<IServiceScopeFactory>(),
            fake,
            Options.Create(options),
            TimeProvider.System,
            NullLogger<OutboxRelay>.Instance);

        return new RelayHarness(connectionString, provider, relay, fake);
    }

    /// <summary>Creates an isolated database for one test and a relay for it.</summary>
    public static async Task<RelayHarness> CreateWithNewDatabaseAsync(PostgresFixture postgres, OutboxOptions? options = null)
    {
        string connectionString = await postgres.CreateIsolatedDatabaseAsync($"relay_{Guid.NewGuid():N}");

        return Create(connectionString, options ?? new OutboxOptions());
    }

    public async Task<Guid> SeedAsync(DateTimeOffset occurredAt, string orderingKey = "checkout-1")
    {
        Guid id = Guid.NewGuid();
        await using CartDbContext context = PostgresFixture.CreateContext(ConnectionString);
        context.OutboxMessages.Add(new OutboxMessage
        {
            Id = id,
            Type = "CartCheckedOut",
            OrderingKey = orderingKey,
            Payload = "{\"specversion\":\"1.0\"}",
            OccurredAt = occurredAt,
        });
        await context.SaveChangesAsync(Token);

        return id;
    }

    public async Task<OutboxMessage> LoadAsync(Guid id)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(ConnectionString);

        return await context.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == id, Token);
    }

    /// <summary>Makes every lease expire at once, instead of waiting for it.</summary>
    public async Task ExpireLeasesAsync()
    {
        await using CartDbContext context = PostgresFixture.CreateContext(ConnectionString);
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE outbox_messages SET locked_until = now() - interval '1 second' WHERE locked_until IS NOT NULL",
            Token);
    }

    /// <summary>Seconds from now until the lease of a message ends, measured by the database clock.</summary>
    public async Task<double> SecondsUntilLeaseEndsAsync(Guid id)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(ConnectionString);

        return await context.Database
            .SqlQuery<double>($"SELECT extract(epoch FROM (locked_until - now()))::float8 AS \"Value\" FROM outbox_messages WHERE id = {id}")
            .SingleAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
    }
}
