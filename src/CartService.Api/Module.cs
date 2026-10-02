namespace CartService.Api;

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

internal static class Module
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return services;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        return app;
    }
}
