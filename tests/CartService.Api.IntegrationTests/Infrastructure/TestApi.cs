namespace CartService.Api.IntegrationTests.Infrastructure;

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using CartService.Api.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

/// <summary>
/// The API under test, running in memory against the PostgreSQL container, with helpers to call it as an
/// anonymous visitor, a signed-in customer, or a guest.
/// </summary>
internal sealed class TestApi : IDisposable
{
    private readonly CartApiFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;

    public TestApi(PostgresFixture postgres, string environment = "Testing", Action<IWebHostBuilder>? configure = null)
    {
        _baseFactory = new CartApiFactory();
        _factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);

            // The settings of the Development environment point to a local database, so tests always pass
            // the container explicitly.
            builder.UseSetting("Database:ConnectionString", postgres.ConnectionString);
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");

            configure?.Invoke(builder);
        });
    }

    /// <summary>The services of the running API, for tests that look at its telemetry providers.</summary>
    public IServiceProvider Services => _factory.Services;

    public HttpClient Anonymous()
    {
        return _factory.CreateClient();
    }

    public HttpClient Customer(Guid customerId)
    {
        return CustomerWithToken(TestTokens.ForCustomer(customerId));
    }

    public HttpClient CustomerWithToken(string accessToken)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    public HttpClient Guest(string guestToken)
    {
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Cart-Token", guestToken);

        return client;
    }

    public void Dispose()
    {
        _factory.Dispose();
        _baseFactory.Dispose();
    }
}
