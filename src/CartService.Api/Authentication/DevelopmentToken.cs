namespace CartService.Api.Authentication;

using System;

internal sealed class DevelopmentToken
{
    public required string AccessToken { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}
