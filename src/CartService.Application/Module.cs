namespace CartService.Application;

using Microsoft.Extensions.DependencyInjection;

public static class Module
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
