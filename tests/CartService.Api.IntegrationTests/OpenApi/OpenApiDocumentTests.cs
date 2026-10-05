namespace CartService.Api.IntegrationTests.OpenApi;

using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Infrastructure;
using CartService.Api.IntegrationTests.Persistence;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OpenApiDocumentTests
{
    private readonly PostgresFixture _postgres;

    public OpenApiDocumentTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InDevelopmentTheDocumentDescribesTheEndpointsAndBothWaysToAuthenticate()
    {
        using TestApi api = new TestApi(_postgres, "Development");
        using HttpClient client = api.Anonymous();

        JsonElement document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Token);

        document.GetProperty("info").GetProperty("title").GetString().ShouldBe("Cart API");
        JsonElement schemes = document.GetProperty("components").GetProperty("securitySchemes");
        schemes.GetProperty("Bearer").GetProperty("scheme").GetString().ShouldBe("bearer");
        schemes.GetProperty("CartToken").GetProperty("name").GetString().ShouldBe("X-Cart-Token");
        JsonElement paths = document.GetProperty("paths");
        paths.TryGetProperty("/v1/carts/{cartId}/items", out _).ShouldBeTrue();
        paths.TryGetProperty("/v1/carts/{cartId}/items/{productId}", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task MyCartAcceptsOnlyABearerTokenWhileOtherCartEndpointsAlsoAcceptTheGuestToken()
    {
        using TestApi api = new TestApi(_postgres, "Development");
        using HttpClient client = api.Anonymous();

        JsonElement paths = (await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Token)).GetProperty("paths");

        SecurityOf(paths, "/v1/carts/me", "get").ShouldBe(new[] { "Bearer" });
        SecurityOf(paths, "/v1/carts/{cartId}", "get").ShouldBe(new[] { "Bearer", "CartToken" });
        SecurityOf(paths, "/v1/carts/{cartId}/items", "post").ShouldBe(new[] { "Bearer", "CartToken" });
        SecurityOf(paths, "/v1/carts", "post").ShouldBeEmpty();
    }

    [Fact]
    public async Task EveryCartEndpointDeclaresItsProblemResponses()
    {
        using TestApi api = new TestApi(_postgres, "Development");
        using HttpClient client = api.Anonymous();

        JsonElement paths = (await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Token)).GetProperty("paths");

        string[] responses = paths.GetProperty("/v1/carts/{cartId}/items").GetProperty("post").GetProperty("responses")
            .EnumerateObject().Select(property => property.Name).Order().ToArray();
        responses.ShouldBe(new[] { "200", "400", "401", "404", "409", "422" });
    }

    [Fact]
    public async Task OutsideDevelopmentNeitherTheDocumentNorTheInteractiveUiIsPublished()
    {
        using TestApi api = new TestApi(_postgres);
        using HttpClient client = api.Anonymous();

        (await client.GetAsync("/openapi/v1.json", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/scalar", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static string[] SecurityOf(JsonElement paths, string path, string method)
    {
        JsonElement operation = paths.GetProperty(path).GetProperty(method);

        if (!operation.TryGetProperty("security", out JsonElement security))
        {
            return [];
        }

        return security.EnumerateArray().Select(requirement => requirement.EnumerateObject().Single().Name).ToArray();
    }
}
