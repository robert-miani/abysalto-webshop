namespace CartService.Infrastructure.Persistence;

using CartService.Domain;
using CartService.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

internal sealed class CartDbContext : DbContext
{
    public CartDbContext(DbContextOptions<CartDbContext> options)
        : base(options)
    {
    }

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CartDbContext).Assembly);
    }
}
