namespace CartService.Application.Abstractions;

using System;

/// <summary>
/// Two requests changed the same cart at the same time and this one lost. The client can read the cart again
/// and retry.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception innerException)
        : base("The cart was changed by another request. Read it again and retry.", innerException)
    {
    }
}
