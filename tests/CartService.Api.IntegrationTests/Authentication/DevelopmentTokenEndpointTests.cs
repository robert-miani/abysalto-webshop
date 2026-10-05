namespace CartService.Api.IntegrationTests.Authentication;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Infrastructure;
using CartService.Api.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DevelopmentTokenEndpointTests
{
    private readonly PostgresFixture _postgres;

    public DevelopmentTokenEndpointTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InDevelopmentTheEndpointIssuesATokenForTheCustomer()
    {
        using CartApiFactory baseFactory = new CartApiFactory();
        using WebApplicationFactory<Program> factory = DevelopmentFactory(baseFactory);
        using HttpClient client = factory.CreateClient();
        Guid customerId = Guid.NewGuid();

        HttpResponseMessage response = await client.PostAsJsonAsync("/dev/token", new { customerId }, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        body.GetProperty("tokenType").GetString().ShouldBe("Bearer");
        body.GetProperty("customerId").GetGuid().ShouldBe(customerId);
        JsonWebToken jwt = new JsonWebToken(body.GetProperty("accessToken").GetString());
        jwt.GetClaim("oid").Value.ShouldBe(customerId.ToString());
    }

    [Fact]
    public async Task AnEmptyCustomerIdIsRejected()
    {
        using CartApiFactory baseFactory = new CartApiFactory();
        using WebApplicationFactory<Program> factory = DevelopmentFactory(baseFactory);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/dev/token", new { customerId = Guid.Empty }, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OutsideDevelopmentTheEndpointDoesNotExist()
    {
        using CartApiFactory factory = new CartApiFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/dev/token", new { customerId = Guid.NewGuid() }, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private WebApplicationFactory<Program> DevelopmentFactory(CartApiFactory baseFactory)
    {
        return baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            // Development settings point to a local database, so the tests use the container instead.
            builder.UseSetting("Database:ConnectionString", _postgres.ConnectionString);
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        });
    }
}
