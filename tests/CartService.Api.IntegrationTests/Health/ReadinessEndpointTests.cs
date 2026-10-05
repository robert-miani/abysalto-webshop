namespace CartService.Api.IntegrationTests.Health;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Infrastructure;
using CartService.Api.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReadinessEndpointTests
{
    private readonly PostgresFixture _postgres;

    public ReadinessEndpointTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AServiceWithAllItsDependenciesIsHealthy()
    {
        string database = await CreateDatabaseAsync();
        using TestApi api = CreateApi(database);

        (HttpStatusCode status, JsonElement report) = await GetReadyAsync(api);

        status.ShouldBe(HttpStatusCode.OK);
        report.GetProperty("status").GetString().ShouldBe("Healthy");
        ChecksOf(report).ShouldBe(new[] { ("outbox", "Healthy"), ("postgres", "Healthy"), ("redis", "Healthy") });
        DescriptionOf(report, "redis").ShouldContain("No cache is configured");
    }

    [Fact]
    public async Task WithoutTheDatabaseTheServiceIsNotReadyButStillAlive()
    {
        using TestApi api = CreateApi("Host=localhost;Port=1;Database=nothing;Username=nobody;Password=secret-password;Timeout=2");

        (HttpStatusCode status, JsonElement report) = await GetReadyAsync(api);
        using HttpClient client = api.Anonymous();
        HttpResponseMessage live = await client.GetAsync("/health/live", Token);

        status.ShouldBe(HttpStatusCode.ServiceUnavailable);
        report.GetProperty("status").GetString().ShouldBe("Unhealthy");
        StatusOf(report, "postgres").ShouldBe("Unhealthy");
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheReportNeverRevealsWhereTheDatabaseIsOrWhyItFailed()
    {
        using TestApi api = CreateApi("Host=db.internal.example;Port=1;Database=nothing;Username=nobody;Password=secret-password;Timeout=2");
        using HttpClient client = api.Anonymous();

        HttpResponseMessage response = await client.GetAsync("/health/ready", Token);
        string body = await response.Content.ReadAsStringAsync(Token);

        body.ShouldNotContain("db.internal.example");
        body.ShouldNotContain("secret-password");
        body.ShouldNotContain("Exception");
    }

    [Fact]
    public async Task WhenRedisIsDownTheServiceIsDegradedButStillReady()
    {
        string database = await CreateDatabaseAsync();
        using TestApi api = CreateApi(database, builder =>
        {
            builder.UseSetting("Cache:ConnectionString", "localhost:1");
            builder.UseSetting("Cache:Timeout", "00:00:00.200");
        });

        (HttpStatusCode status, JsonElement report) = await GetReadyAsync(api);

        status.ShouldBe(HttpStatusCode.OK);
        report.GetProperty("status").GetString().ShouldBe("Degraded");
        StatusOf(report, "redis").ShouldBe("Degraded");
        StatusOf(report, "postgres").ShouldBe("Healthy");
    }

    [Fact]
    public async Task EventsThatWaitForTooLongMakeTheServiceDegraded()
    {
        string database = await CreateDatabaseAsync();
        await InsertOutboxMessageAsync(database, DateTimeOffset.UtcNow.AddMinutes(-10), processedAt: null, attempts: 0);
        using TestApi api = CreateApi(database);

        (HttpStatusCode status, JsonElement report) = await GetReadyAsync(api);

        status.ShouldBe(HttpStatusCode.OK);
        report.GetProperty("status").GetString().ShouldBe("Degraded");
        StatusOf(report, "outbox").ShouldBe("Degraded");
        DescriptionOf(report, "outbox").ShouldContain("wait for longer than expected");
    }

    [Fact]
    public async Task AnEventThatCouldNotBePublishedMakesTheServiceDegraded()
    {
        string database = await CreateDatabaseAsync();
        await InsertOutboxMessageAsync(database, DateTimeOffset.UtcNow, processedAt: null, attempts: 10);
        using TestApi api = CreateApi(database);

        (_, JsonElement report) = await GetReadyAsync(api);

        StatusOf(report, "outbox").ShouldBe("Degraded");
        DescriptionOf(report, "outbox").ShouldContain("could not be published");
    }

    [Fact]
    public async Task ARecentEventAndOldProcessedEventsAreFine()
    {
        string database = await CreateDatabaseAsync();
        await InsertOutboxMessageAsync(database, DateTimeOffset.UtcNow.AddSeconds(-5), processedAt: null, attempts: 1);
        await InsertOutboxMessageAsync(database, DateTimeOffset.UtcNow.AddDays(-3), processedAt: DateTimeOffset.UtcNow.AddDays(-3), attempts: 1);
        using TestApi api = CreateApi(database);

        (_, JsonElement report) = await GetReadyAsync(api);

        StatusOf(report, "outbox").ShouldBe("Healthy");
        report.GetProperty("status").GetString().ShouldBe("Healthy");
    }

    private static async Task<(HttpStatusCode Status, JsonElement Report)> GetReadyAsync(TestApi api)
    {
        using HttpClient client = api.Anonymous();
        HttpResponseMessage response = await client.GetAsync("/health/ready", Token);
        JsonElement report = await response.Content.ReadFromJsonAsync<JsonElement>(Token);

        return (response.StatusCode, report);
    }

    private static (string Name, string Status)[] ChecksOf(JsonElement report)
    {
        return report.GetProperty("checks").EnumerateArray()
            .Select(check => (check.GetProperty("name").GetString()!, check.GetProperty("status").GetString()!))
            .OrderBy(check => check.Item1, StringComparer.Ordinal)
            .ToArray();
    }

    private static string StatusOf(JsonElement report, string name)
    {
        return ChecksOf(report).Single(check => check.Name == name).Status;
    }

    private static string DescriptionOf(JsonElement report, string name)
    {
        return report.GetProperty("checks").EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == name)
            .GetProperty("description").GetString()!;
    }

    private static async Task InsertOutboxMessageAsync(string connectionString, DateTimeOffset occurredAt, DateTimeOffset? processedAt, int attempts)
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using NpgsqlCommand command = new NpgsqlCommand(
            """
            INSERT INTO outbox_messages (id, type, ordering_key, payload, occurred_at, processed_at, attempts)
            VALUES (@id, 'cart.checked-out', 'key', '{}'::jsonb, @occurredAt, @processedAt, @attempts)
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("occurredAt", occurredAt);
        command.Parameters.AddWithValue("processedAt", (object?)processedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("attempts", attempts);
        await command.ExecuteNonQueryAsync(Token);
    }

    // Each test has its own database, because the outbox check looks at every row of the table.
    private Task<string> CreateDatabaseAsync()
    {
        return _postgres.CreateIsolatedDatabaseAsync($"ready_{Guid.NewGuid():N}");
    }

    private TestApi CreateApi(string connectionString, Action<IWebHostBuilder>? configure = null)
    {
        return new TestApi(_postgres, configure: builder =>
        {
            builder.UseSetting("Database:ConnectionString", connectionString);
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            configure?.Invoke(builder);
        });
    }
}
