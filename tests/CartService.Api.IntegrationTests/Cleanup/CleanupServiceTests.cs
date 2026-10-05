namespace CartService.Api.IntegrationTests.Cleanup;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Persistence;
using CartService.Application;
using CartService.Domain;
using CartService.Infrastructure.Cleanup;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CleanupServiceTests
{
    private readonly PostgresFixture _postgres;

    public CleanupServiceTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OneRunDeletesWhatIsStaleAndReportsIt()
    {
        string database = await _postgres.CreateIsolatedDatabaseAsync($"cleanup_{Guid.NewGuid():N}");
        await AddAbandonedGuestCartAsync(database);
        await using ServiceProvider provider = CreateProvider(database);
        CleanupService service = CreateService(provider, new CleanupOptions());

        CleanupResult result = await service.RunOnceAsync(Token);

        result.Carts.ShouldBe(1);
        (await CountCartsAsync(database)).ShouldBe(0);
    }

    [Fact]
    public async Task ARunIsASpanThatSaysWhatItDeleted()
    {
        string database = await _postgres.CreateIsolatedDatabaseAsync($"cleanup_{Guid.NewGuid():N}");
        await AddAbandonedGuestCartAsync(database);
        await using ServiceProvider provider = CreateProvider(database);
        CleanupService service = CreateService(provider, new CleanupOptions());
        List<Activity> stopped = new List<Activity>();
        using ActivityListener listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CartTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (stopped)
                {
                    stopped.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        await service.RunOnceAsync(Token);

        Activity span;

        lock (stopped)
        {
            span = stopped.Single(activity => activity.OperationName == "cleanup" && Equals(activity.GetTagItem("cleanup.deleted.carts"), 1));
        }

        span.Kind.ShouldBe(ActivityKind.Internal);
    }

    [Fact]
    public async Task TheBackgroundLoopRunsTheCleanup()
    {
        string database = await _postgres.CreateIsolatedDatabaseAsync($"cleanup_{Guid.NewGuid():N}");
        await AddAbandonedGuestCartAsync(database);
        await using ServiceProvider provider = CreateProvider(database);
        CleanupService service = CreateService(provider, new CleanupOptions { InitialDelay = TimeSpan.Zero, Interval = TimeSpan.FromMinutes(1) });

        await service.StartAsync(Token);

        try
        {
            await EventuallyAsync(async () => await CountCartsAsync(database) == 0);
        }
        finally
        {
            await service.StopAsync(Token);
        }
    }

    [Fact]
    public async Task ADisabledJobDeletesNothing()
    {
        string database = await _postgres.CreateIsolatedDatabaseAsync($"cleanup_{Guid.NewGuid():N}");
        await AddAbandonedGuestCartAsync(database);
        await using ServiceProvider provider = CreateProvider(database);
        CleanupService service = CreateService(provider, new CleanupOptions { Enabled = false, InitialDelay = TimeSpan.Zero });

        await service.StartAsync(Token);
        await Task.Delay(TimeSpan.FromSeconds(1), Token);
        await service.StopAsync(Token);

        (await CountCartsAsync(database)).ShouldBe(1);
    }

    [Fact]
    public async Task AFailedRunDoesNotStopTheJob()
    {
        await using ServiceProvider provider = CreateProvider("Host=localhost;Port=1;Database=nothing;Username=nobody;Password=x;Timeout=1");
        CleanupService service = CreateService(provider, new CleanupOptions { InitialDelay = TimeSpan.Zero });

        await service.StartAsync(Token);
        await Task.Delay(TimeSpan.FromSeconds(2), Token);

        // The job logged the failure and waits for the next interval instead of crashing the host.
        Task job = service.ExecuteTask ?? throw new InvalidOperationException("The job did not start.");
        job.IsFaulted.ShouldBeFalse();
        job.IsCompleted.ShouldBeFalse();
        await service.StopAsync(Token);
    }

    private static CleanupService CreateService(ServiceProvider provider, CleanupOptions options)
    {
        return new CleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            TimeProvider.System,
            NullLogger<CleanupService>.Instance);
    }

    private static ServiceProvider CreateProvider(string connectionString)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddDbContext<CartDbContext>(builder => builder.UseCartDatabase(connectionString));
        services.AddSingleton(Options.Create(new CleanupOptions()));
        services.AddScoped<CleanupStore>();

        return services.BuildServiceProvider();
    }

    private static async Task AddAbandonedGuestCartAsync(string database)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(database);
        DateTimeOffset longAgo = DateTimeOffset.UtcNow.AddDays(-60);
        Cart cart = Cart.CreateForGuest(Guid.NewGuid().ToString("N"), longAgo);
        cart.AddItem("cap-red", "Red cap", Money.Eur(9.50m), 1, longAgo);
        context.Carts.Add(cart);
        await context.SaveChangesAsync(Token);
    }

    private static async Task<int> CountCartsAsync(string database)
    {
        await using CartDbContext context = PostgresFixture.CreateContext(database);

        return await context.Carts.CountAsync(Token);
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);

        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition did not become true in time.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), Token);
        }
    }
}
