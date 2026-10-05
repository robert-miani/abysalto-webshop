namespace CartService.Api.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

public sealed class CartApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The key that signs the tokens of the tests. The API trusts it only in non-production environments.</summary>
    public const string SigningKey = "test-signing-key-for-integration-tests-0123456789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // The settings are validated at startup. Tests that do not touch the database never connect to it.
        builder.UseSetting("Database:ConnectionString", "Host=localhost;Database=cartservice_not_used");
        builder.UseSetting("Authentication:DevelopmentSigningKey", SigningKey);

        // Most tests do not need a message broker, so the relay is off. A test that does need it turns it on.
        builder.UseSetting("Outbox:Enabled", "false");

        // The cleanup job deletes by age and is tested on its own.
        builder.UseSetting("Cleanup:Enabled", "false");

        // The Development settings point to a Redis on localhost. Tests have no cache unless they ask for one.
        builder.UseSetting("Cache:ConnectionString", string.Empty);

        // Tests send many requests from one address. The tests of the limits turn them on again.
        builder.UseSetting("RateLimiting:Enabled", "false");
    }
}
