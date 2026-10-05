namespace CartService.Application.Tests;

using System;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using CartService.Application.Tests.Fakes;
using CartService.Domain;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class CachedCartTests
{
    [Fact]
    public void ACustomerCartIsOwnedOnlyByThatCustomer()
    {
        Guid customerId = Guid.NewGuid();
        CachedCart cached = Cache(Cart.CreateForCustomer(customerId, HandlerEnvironment.Start));

        cached.IsOwnedBy(Requester.ForCustomer(customerId)).ShouldBeTrue();
        cached.IsOwnedBy(Requester.ForCustomer(Guid.NewGuid())).ShouldBeFalse();
        cached.IsOwnedBy(Requester.ForGuest("hash")).ShouldBeFalse();
    }

    [Fact]
    public void AGuestCartIsOwnedOnlyByTheHolderOfItsToken()
    {
        CachedCart cached = Cache(Cart.CreateForGuest("hash-of-token", HandlerEnvironment.Start));

        cached.IsOwnedBy(Requester.ForGuest("hash-of-token")).ShouldBeTrue();
        cached.IsOwnedBy(Requester.ForGuest("another-hash")).ShouldBeFalse();
        cached.IsOwnedBy(Requester.ForCustomer(Guid.NewGuid())).ShouldBeFalse();
    }

    [Fact]
    public void TheCachedOwnerFollowsTheSameRuleAsTheCart()
    {
        Cart cart = Cart.CreateForCustomer(Guid.NewGuid(), HandlerEnvironment.Start);
        CachedCart cached = Cache(cart);
        Requester[] requesters = { Requester.ForCustomer(cart.CustomerId!.Value), Requester.ForCustomer(Guid.NewGuid()), Requester.ForGuest("x") };

        foreach (Requester requester in requesters)
        {
            cached.IsOwnedBy(requester).ShouldBe(requester.Owns(cart));
        }
    }

    private static CachedCart Cache(Cart cart)
    {
        return CachedCart.From(cart, CartDto.From(cart));
    }
}
