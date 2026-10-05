namespace CartService.Api.IntegrationTests.Guests;

using System.Collections.Generic;
using CartService.Application.Abstractions;
using CartService.Infrastructure.Guests;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class GuestTokenServiceTests
{
    private readonly GuestTokenService _service = new GuestTokenService();

    [Fact]
    public void ATokenIs256BitsOfUrlSafeText()
    {
        GuestToken token = _service.Create();

        // 32 bytes are 43 characters in URL-safe base64 without padding.
        token.Value.Length.ShouldBe(43);
        token.Value.ShouldMatch("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void TheStoredHashIsNotTheTokenAndFitsTheDatabaseColumn()
    {
        GuestToken token = _service.Create();

        token.Hash.ShouldNotBe(token.Value);
        token.Hash.Length.ShouldBeLessThanOrEqualTo(64);
        token.Hash.ShouldMatch("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void HashingTheTokenAgainGivesTheStoredHash()
    {
        GuestToken token = _service.Create();

        _service.Hash(token.Value).ShouldBe(token.Hash);
    }

    [Fact]
    public void DifferentTokensHaveDifferentHashes()
    {
        _service.Hash("token-a").ShouldNotBe(_service.Hash("token-b"));
    }

    [Fact]
    public void ManyTokensAreAllDifferent()
    {
        HashSet<string> tokens = new HashSet<string>();

        for (int index = 0; index < 1000; index++)
        {
            tokens.Add(_service.Create().Value).ShouldBeTrue();
        }
    }
}
