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
        SecurityTokenDescriptor descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(new[] { new Claim("oid", customerId.ToString()) }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
