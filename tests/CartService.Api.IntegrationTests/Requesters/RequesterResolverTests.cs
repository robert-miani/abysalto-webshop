namespace CartService.Api.IntegrationTests.Requesters;

using System;
using System.Security.Claims;
using CartService.Api.Authentication;
using CartService.Api.Requesters;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using CartService.Domain;
using CartService.Infrastructure.Guests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class RequesterResolverTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    private readonly GuestTokenService _guestTokens = new GuestTokenService();
    private readonly RequesterResolver _resolver;

    public RequesterResolverTests()
    {
        _resolver = new RequesterResolver(_guestTokens, Options.Create(new CartAuthenticationOptions { Audience = "api" }));
    }

    [Fact]
    public void AValidatedTokenWithAnOidClaimIsACustomer()
    {
        Guid customerId = Guid.NewGuid();
        DefaultHttpContext context = ContextWithCustomer(customerId.ToString());

        Requester? requester = _resolver.Resolve(context);

        requester.ShouldNotBeNull();
        requester.CustomerId.ShouldBe(customerId);
        requester.GuestTokenHash.ShouldBeNull();
    }

    [Fact]
    public void ATokenWithoutAUsableCustomerIdIsNotACustomer()
    {
        _resolver.Resolve(ContextWithCustomer("not-a-guid")).ShouldBeNull();
        _resolver.Resolve(ContextWithCustomer(Guid.Empty.ToString())).ShouldBeNull();
        _resolver.Resolve(ContextWithCustomer(null)).ShouldBeNull();
    }

    [Fact]
    public void TheSubClaimIsNotTheCustomerId()
    {
        DefaultHttpContext context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test")),
        };

        _resolver.Resolve(context).ShouldBeNull();
    }

    [Fact]
    public void ACartTokenHeaderIsAGuest()
    {
        GuestToken token = CreateToken();
        DefaultHttpContext context = new DefaultHttpContext();
        context.Request.Headers[RequesterResolver.GuestTokenHeader] = token.Value;

        Requester? requester = _resolver.Resolve(context);

        requester.ShouldNotBeNull();
        requester.CustomerId.ShouldBeNull();
        requester.GuestTokenHash.ShouldBe(token.Hash);
    }

    [Fact]
    public void TheGuestOwnsTheCartOfTheirTokenAndNoOtherGuestCart()
    {
        GuestToken token = CreateToken();
        Cart ownCart = Cart.CreateForGuest(token.Hash, Now);
        Cart otherCart = Cart.CreateForGuest(CreateToken().Hash, Now);
        DefaultHttpContext context = new DefaultHttpContext();
        context.Request.Headers[RequesterResolver.GuestTokenHeader] = token.Value;

        Requester requester = _resolver.Resolve(context)!;

        requester.Owns(ownCart).ShouldBeTrue();
        requester.Owns(otherCart).ShouldBeFalse();
    }

    [Fact]
    public void WhenBothArePresentTheCustomerWins()
    {
        Guid customerId = Guid.NewGuid();
        DefaultHttpContext context = ContextWithCustomer(customerId.ToString());
        context.Request.Headers[RequesterResolver.GuestTokenHeader] = CreateToken().Value;

        Requester? requester = _resolver.Resolve(context);

        requester!.CustomerId.ShouldBe(customerId);
    }

    [Fact]
    public void NothingProvedMeansNoRequester()
    {
        _resolver.Resolve(new DefaultHttpContext()).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankCartTokenMeansNoRequester(string value)
    {
        DefaultHttpContext context = new DefaultHttpContext();
        context.Request.Headers[RequesterResolver.GuestTokenHeader] = value;

        _resolver.Resolve(context).ShouldBeNull();
    }

    private static DefaultHttpContext ContextWithCustomer(string? oid)
    {
        ClaimsIdentity identity = new ClaimsIdentity("test");

        if (oid is not null)
        {
            identity.AddClaim(new Claim("oid", oid));
        }

        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private GuestToken CreateToken()
    {
        return _guestTokens.Create();
    }
}
