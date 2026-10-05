namespace CartService.Application.Abstractions;

/// <summary>
/// Creates the secret tokens that identify guest carts. Only the hash of a token is stored, so a leaked
/// database does not leak access to carts.
/// </summary>
public interface IGuestTokenService
{
    GuestToken Create();

    /// <summary>
    /// Returns the hash that is stored for a token, so a token sent by a client can be compared with it.
    /// </summary>
    string Hash(string token);
}
