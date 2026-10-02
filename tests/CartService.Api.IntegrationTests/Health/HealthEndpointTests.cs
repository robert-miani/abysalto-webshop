namespace CartService.Api.IntegrationTests.Health;

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

[Trait("Category", "Integration")]
public sealed class HealthEndpointTests : IClassFixture<CartApiFactory>
{
    private readonly CartApiFactory _factory;

    public HealthEndpointTests(CartApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LivenessReturnsHealthy()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldBe("Healthy");
    }
}
