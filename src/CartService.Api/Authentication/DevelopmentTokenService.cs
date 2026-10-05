namespace CartService.Api.Authentication;

using System;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Issues tokens for local use, standing in for the identity provider. They are signed with the development
/// key and carry the same customer id claim that Entra External ID tokens carry.
/// </summary>
internal sealed class DevelopmentTokenService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly IOptions<CartAuthenticationOptions> _options;
    private readonly TimeProvider _time;

    public DevelopmentTokenService(IOptions<CartAuthenticationOptions> options, TimeProvider time)
    {
        _options = options;
        _time = time;
    }

    public DevelopmentToken Create(Guid customerId)
    {
        CartAuthenticationOptions options = _options.Value;

        if (string.IsNullOrWhiteSpace(options.DevelopmentSigningKey))
        {
            throw new InvalidOperationException("No development signing key is configured.");
        }

        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset expires = now.Add(Lifetime);

        SecurityTokenDescriptor descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.DevelopmentIssuer,
            Audience = options.Audience,
            Subject = new ClaimsIdentity(new[] { new Claim(options.CustomerIdClaim, customerId.ToString()) }),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.DevelopmentSigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new DevelopmentToken
        {
            AccessToken = new JsonWebTokenHandler().CreateToken(descriptor),
            ExpiresAt = expires,
        };
    }
}
