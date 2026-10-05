namespace CartService.Api.Authentication;

using System;

internal sealed class DevelopmentTokenRequest
{
    public Guid CustomerId { get; init; }
}
