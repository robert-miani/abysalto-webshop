namespace CartService.Infrastructure;

using System;
using CartService.Application.Abstractions;
using CartService.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public static class Module
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<CartDbContext>((serviceProvider, options) =>
        {
            DatabaseOptions database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            options.UseCartDatabase(database.ConnectionString);
        });

        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
