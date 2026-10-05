namespace CartService.Infrastructure.Persistence.Configurations;

using CartService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("carts", table =>
        {
            // A cart belongs to a customer or to a guest, never to both and never to nobody.
            table.HasCheckConstraint("ck_carts_single_owner", "(customer_id IS NULL) <> (guest_token_hash IS NULL)");
        });

        builder.HasKey(cart => cart.Id);
        builder.Property(cart => cart.Id).ValueGeneratedNever();

        builder.Property(cart => cart.GuestTokenHash).HasMaxLength(64);
        builder.Property(cart => cart.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(cart => cart.CreatedAt).IsRequired();
        builder.Property(cart => cart.UpdatedAt).IsRequired();

        // The domain increments the version on every change, so EF Core rejects an update when another request
        // changed the cart after it was read.
        builder.Property(cart => cart.Version).IsConcurrencyToken();

        // A customer has at most one active cart, also when two requests create it at the same time.
        builder.HasIndex(cart => cart.CustomerId)
            .IsUnique()
            .HasDatabaseName("ux_carts_active_customer")
            .HasFilter("customer_id IS NOT NULL AND status = 'Active'");

        // A guest cart is found by the hash of its token, and no two carts can share a token.
        builder.HasIndex(cart => cart.GuestTokenHash)
            .IsUnique()
            .HasDatabaseName("ux_carts_guest_token_hash")
            .HasFilter("guest_token_hash IS NOT NULL");

        // The lines belong to the cart: they are loaded with it and deleted with it.
        builder.HasMany(cart => cart.Items)
            .WithOne()
            .HasForeignKey(CartItemConfiguration.CartIdColumn)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(cart => cart.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }
}
