namespace CartService.Infrastructure.Persistence.Configurations;

using System;
using CartService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    /// <summary>The foreign key to the cart. It exists only in the database, not in the domain model.</summary>
    public const string CartIdColumn = "CartId";

    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("cart_items", table =>
        {
            table.HasCheckConstraint("ck_cart_items_quantity", $"quantity BETWEEN 1 AND {CartLimits.MaxQuantityPerItem}");
        });

        builder.Property<Guid>(CartIdColumn);
        builder.HasKey(CartIdColumn, nameof(CartItem.ProductId));

        builder.Property(item => item.ProductId).HasMaxLength(64);
        builder.Property(item => item.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Quantity).IsRequired();

        // Money is a value object. As a complex type it is copied by value, so two lines can hold the same amount
        // without sharing one object. It is stored as two columns of the line.
        builder.ComplexProperty(item => item.UnitPrice, price =>
        {
            price.Property(money => money.Amount).HasColumnName("unit_price").HasPrecision(18, 2);
            price.Property(money => money.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength();
        });
    }
}
