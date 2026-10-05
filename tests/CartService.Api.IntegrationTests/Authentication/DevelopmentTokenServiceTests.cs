namespace CartService.Api.IntegrationTests.Authentication;

using System;
using System.Text;
using System.Threading.Tasks;
using CartService.Api.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class DevelopmentTokenServiceTests
{
    private const string Key = "a-development-key-that-is-long-enough-0123";
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new FakeTimeProvider(Now);

    [Fact]
    public async Task TheTokenCarriesTheCustomerIdInTheOidClaimAndIsValidForTheApi()
    {
        Guid customerId = Guid.NewGuid();
        CartAuthenticationOptions options = new CartAuthenticationOptions { Audience = "cartservice-api", DevelopmentSigningKey = Key };

        DevelopmentToken token = CreateService(options).Create(customerId);

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, ValidationParameters(options));
        result.IsValid.ShouldBeTrue();
        result.Claims["oid"].ShouldBe(customerId.ToString());
        result.Claims.ContainsKey("sub").ShouldBeFalse();
    }

    [Fact]
    public void TheTokenIsValidForOneHour()
    {
        CartAuthenticationOptions options = new CartAuthenticationOptions { Audience = "cartservice-api", DevelopmentSigningKey = Key };

        DevelopmentToken token = CreateService(options).Create(Guid.NewGuid());

        token.ExpiresAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public async Task AnExpiredTokenIsRejected()
    {
        CartAuthenticationOptions options = new CartAuthenticationOptions { Audience = "cartservice-api", DevelopmentSigningKey = Key };
        DevelopmentToken token = CreateService(options).Create(Guid.NewGuid());
        _time.Advance(TimeSpan.FromHours(2));

        TokenValidationParameters parameters = ValidationParameters(options);
        parameters.LifetimeValidator = (notBefore, expires, _, _) => expires > _time.GetUtcNow().UtcDateTime;
        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, parameters);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task ATokenForAnotherAudienceIsRejected()
    {
        CartAuthenticationOptions issued = new CartAuthenticationOptions { Audience = "another-api", DevelopmentSigningKey = Key };
        CartAuthenticationOptions expected = new CartAuthenticationOptions { Audience = "cartservice-api", DevelopmentSigningKey = Key };
        DevelopmentToken token = CreateService(issued).Create(Guid.NewGuid());

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, ValidationParameters(expected));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void WithoutASigningKeyNoTokenCanBeIssued()
    {
        CartAuthenticationOptions options = new CartAuthenticationOptions { Audience = "cartservice-api" };

        Should.Throw<InvalidOperationException>(() => CreateService(options).Create(Guid.NewGuid()));
    }

    private static TokenValidationParameters ValidationParameters(CartAuthenticationOptions options)
    {
        return new TokenValidationParameters
        {
            ValidIssuer = options.DevelopmentIssuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.DevelopmentSigningKey ?? Key)),
            ValidateLifetime = false,
        };
    }

    private DevelopmentTokenService CreateService(CartAuthenticationOptions options)
    {
        return new DevelopmentTokenService(Options.Create(options), _time);
    }
}
