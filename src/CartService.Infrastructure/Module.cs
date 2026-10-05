namespace CartService.Infrastructure;

using System;
using CartService.Application.Abstractions;
using CartService.Infrastructure.Catalog;
using CartService.Infrastructure.Guests;
using CartService.Infrastructure.Outbox;
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

        services.AddOptions<ProductCatalogOptions>()
            .BindConfiguration(ProductCatalogOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ProductCatalogOptions>, ProductCatalogOptionsValidator>();

        services.AddSingleton<IProductCatalog, ConfiguredProductCatalog>();
        services.AddSingleton<IGuestTokenService, GuestTokenService>();

        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutbox, OutboxWriter>();
        services.AddHostedService<DatabaseMigrationService>();

        return services;
    }
}
