namespace CartService.Api.Authentication;

using System;

internal sealed class DevelopmentTokenResponse
{
    public required string AccessToken { get; init; }

    public required string TokenType { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required Guid CustomerId { get; init; }
}
