namespace CartService.Application.Abstractions;

using System;

/// <summary>
/// A customer has at most one active cart. Two requests created one at the same time and this one lost.
/// </summary>
public sealed class ActiveCartAlreadyExistsException : Exception
{
    public ActiveCartAlreadyExistsException(Exception innerException)
        : base("The customer already has an active cart.", innerException)
    {
    }
}
