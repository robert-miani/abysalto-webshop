namespace CartService.Api;

using System.Threading.Tasks;
using CartService.Application;
using CartService.Infrastructure;
using Microsoft.AspNetCore.Builder;

public sealed class Program
{
    private Program()
    {
    }

    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Services
            .AddApplication()
            .AddInfrastructure(builder.Configuration)
            .AddApi(builder.Configuration);

        WebApplication app = builder.Build();

        app.UseApi();

        await app.RunAsync();
    }
}
