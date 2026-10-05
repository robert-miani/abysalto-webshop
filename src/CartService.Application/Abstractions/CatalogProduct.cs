namespace CartService.Application.Abstractions;

using CartService.Domain;

public sealed class CatalogProduct
{
    public required string ProductId { get; init; }

    public required string Name { get; init; }

    public required Money UnitPrice { get; init; }
}
