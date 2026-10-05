namespace CartService.Api;

using System;
using System.Text.Json.Serialization;
using CartService.Api.Authentication;
using CartService.Api.Carts;
using CartService.Api.ErrorHandling;
using CartService.Api.Health;
using CartService.Api.OpenApi;
using CartService.Api.RateLimiting;
using CartService.Api.Requesters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal static class Module
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddProblemDetails();
        services.AddValidation();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddHealthChecks();
        services.AddCartAuthentication();
        services.AddCartRateLimiting();
        services.AddSingleton<RequesterResolver>();
        services.AddCartOpenApi();

        // The cart status is sent as text ("Active"), not as a number.
        services.Configure<JsonOptions>(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        return services;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapHealthEndpoints();
        app.MapCartEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapDevelopmentTokenEndpoint();
            app.MapCartOpenApi();
        }

        return app;
    }
}
