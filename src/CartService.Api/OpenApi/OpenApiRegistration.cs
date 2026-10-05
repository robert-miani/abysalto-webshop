namespace CartService.Api.OpenApi;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;

internal static class OpenApiRegistration
{
    public static IServiceCollection AddCartOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options => options.AddDocumentTransformer<CartOpenApiDocumentTransformer>());

        return services;
    }

    /// <summary>
    /// Publishes the OpenAPI document at /openapi/v1.json and the interactive documentation at /scalar. Only
    /// the Development environment calls this.
    /// </summary>
    public static WebApplication MapCartOpenApi(this WebApplication app)
    {
        app.MapOpenApi();
        app.MapScalarApiReference("/scalar", options =>
        {
            options.WithTitle("Cart API");
            options.AddPreferredSecuritySchemes("Bearer");
        });

        return app;
    }
}
