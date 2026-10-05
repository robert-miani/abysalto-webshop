namespace CartService.Api.IntegrationTests.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

public sealed class CartApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // The settings are validated at startup. Tests that do not touch the database never connect to it.
        builder.UseSetting("Database:ConnectionString", "Host=localhost;Database=cartservice_not_used");
    }
}
