namespace CartService.Infrastructure.Guests;

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using CartService.Application.Abstractions;

/// <summary>
/// A guest token is 32 random bytes (256 bits) in URL-safe text. Because the token is random and long, a plain
/// SHA-256 hash is enough to store it: nobody can guess a token from its hash.
/// </summary>
internal sealed class GuestTokenService : IGuestTokenService
{
    private const int TokenSizeInBytes = 32;

    public GuestToken Create()
    {
        string token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenSizeInBytes));

        return new GuestToken { Value = token, Hash = Hash(token) };
    }

    public string Hash(string token)
    {
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
