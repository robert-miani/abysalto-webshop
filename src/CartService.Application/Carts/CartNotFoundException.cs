namespace CartService.Application.Carts;

using System;

/// <summary>
/// The cart does not exist, or it belongs to somebody else. Both cases look the same on purpose, so nobody can
/// find out which cart ids exist.
/// </summary>
public sealed class CartNotFoundException : Exception
{
    public CartNotFoundException()
        : base("The cart was not found.")
    {
    }
}
