namespace CartService.Api.IntegrationTests.Infrastructure;

using System;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Creates bearer tokens the way an identity provider would, so tests do not depend on the development token
/// endpoint of the service.
/// </summary>
internal static class TestTokens
{
    public const string Issuer = "cartservice-dev";
    public const string Audience = "cartservice-api";

    public static string ForCustomer(
        Guid customerId,
        string signingKey = CartApiFactory.SigningKey,
        string audience = Audience)
    {
        return Create(customerId, signingKey, audience, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1));
    }

    /// <summary>A correctly signed token that expired an hour ago.</summary>
    public static string ExpiredFor(Guid customerId)
    {
        return Create(customerId, CartApiFactory.SigningKey, Audience, DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1));
    }

    private static string Create(Guid customerId, string signingKey, string audience, DateTime notBefore, DateTime expires)
    {
        SecurityTokenDescriptor descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(new[] { new Claim("oid", customerId.ToString()) }),
            NotBefore = notBefore,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
