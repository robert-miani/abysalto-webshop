namespace CartService.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Lets the `dotnet ef` tool create the context without starting the API. Creating a migration does not
/// connect to the database, so the connection string only has to be well formed.
/// </summary>
internal sealed class CartDbContextFactory : IDesignTimeDbContextFactory<CartDbContext>
{
    public CartDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<CartDbContext> builder = new DbContextOptionsBuilder<CartDbContext>();
        builder.UseCartDatabase("Host=localhost;Database=cartservice_design");

        return new CartDbContext(builder.Options);
    }
}
