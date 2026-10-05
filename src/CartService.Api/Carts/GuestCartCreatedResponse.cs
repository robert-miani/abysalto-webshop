namespace CartService.Api.Carts;

using CartService.Application.Carts;

/// <summary>
/// The answer to creating a guest cart. The token is the only proof of ownership, and this is the only time it
/// is shown: the service stores just its hash.
/// </summary>
internal sealed class GuestCartCreatedResponse
{
    public required string GuestToken { get; init; }

    public required CartDto Cart { get; init; }
}
