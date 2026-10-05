namespace CartService.Domain;

using System;

/// <summary>
/// A request that is well formed but breaks a business rule of the cart.
/// </summary>
public sealed class CartRuleViolationException : Exception
{
    public CartRuleViolationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
