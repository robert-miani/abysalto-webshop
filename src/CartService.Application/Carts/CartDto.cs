namespace CartService.Application.Carts;

using System;
using System.Collections.Generic;
using System.Linq;
using CartService.Domain;

/// <summary>
/// The cart as clients see it. All amounts are in <see cref="Currency"/>.
/// </summary>
public sealed class CartDto
{
    public required Guid Id { get; init; }

    public required CartStatus Status { get; init; }

    public required IReadOnlyList<CartItemDto> Items { get; init; }

    public required decimal Total { get; init; }

    public required string Currency { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public static CartDto From(Cart cart)
    {
        return new CartDto
        {
            Id = cart.Id,
            Status = cart.Status,
            Items = cart.Items.Select(CartItemDto.From).OrderBy(item => item.ProductId, StringComparer.Ordinal).ToList(),
            Total = cart.Total.Amount,
            Currency = cart.Total.Currency,
            CreatedAt = cart.CreatedAt,
            UpdatedAt = cart.UpdatedAt,
        };
    }
}
