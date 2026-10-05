namespace CartService.Application;

using System;
using CartService.Application.Carts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class Module
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<CreateCartHandler>();
        services.AddScoped<GetCartHandler>();
        services.AddScoped<GetMyCartHandler>();

        return services;
    }
}
