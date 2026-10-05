namespace CartService.Api.IntegrationTests.Infrastructure;

using System.Text.Json;
using System.Text.Json.Serialization;

internal static class TestJson
{
    /// <summary>Reads responses the way the API writes them: camelCase, with enums as text.</summary>
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        JsonSerializerOptions options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
