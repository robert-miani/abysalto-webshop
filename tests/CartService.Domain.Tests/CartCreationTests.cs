namespace CartService.Domain.Tests;

using System;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class CartCreationTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void CustomerCartStartsActiveAndEmpty()
    {
        Guid customerId = Guid.NewGuid();

        Cart cart = Cart.CreateForCustomer(customerId, Now);

        cart.Id.ShouldNotBe(Guid.Empty);
        cart.CustomerId.ShouldBe(customerId);
        cart.GuestTokenHash.ShouldBeNull();
        cart.Status.ShouldBe(CartStatus.Active);
        cart.Items.ShouldBeEmpty();
        cart.CheckoutId.ShouldBeNull();
        cart.CreatedAt.ShouldBe(Now);
        cart.UpdatedAt.ShouldBe(Now);
        cart.Version.ShouldBe(0);
        cart.Total.ShouldBe(Money.Zero("EUR"));
    }

    [Fact]
    public void GuestCartStartsActiveAndEmptyWithoutACustomer()
    {
        Cart cart = Cart.CreateForGuest("hash-of-token", Now);

        cart.CustomerId.ShouldBeNull();
        cart.GuestTokenHash.ShouldBe("hash-of-token");
        cart.Status.ShouldBe(CartStatus.Active);
        cart.Items.ShouldBeEmpty();
    }

    [Fact]
    public void EveryCartGetsItsOwnId()
    {
        Cart first = Cart.CreateForGuest("a", Now);
        Cart second = Cart.CreateForGuest("a", Now);

        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    public void CustomerCartRejectsAnEmptyCustomerId()
    {
        Should.Throw<ArgumentException>(() => Cart.CreateForCustomer(Guid.Empty, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GuestCartRejectsABlankTokenHash(string tokenHash)
    {
        Should.Throw<ArgumentException>(() => Cart.CreateForGuest(tokenHash, Now));
    }

    [Fact]
    public void CustomerCartIsOwnedOnlyByItsCustomer()
    {
        Guid customerId = Guid.NewGuid();
        Cart cart = Cart.CreateForCustomer(customerId, Now);

        cart.IsOwnedByCustomer(customerId).ShouldBeTrue();
        cart.IsOwnedByCustomer(Guid.NewGuid()).ShouldBeFalse();
        cart.IsOwnedByGuest("any-token-hash").ShouldBeFalse();
    }

    [Fact]
    public void GuestCartIsOwnedOnlyByTheMatchingTokenHash()
    {
        Cart cart = Cart.CreateForGuest("hash-of-token", Now);

        cart.IsOwnedByGuest("hash-of-token").ShouldBeTrue();
        cart.IsOwnedByGuest("another-hash").ShouldBeFalse();
        cart.IsOwnedByGuest("hash-of-token-and-more").ShouldBeFalse();
        cart.IsOwnedByCustomer(Guid.NewGuid()).ShouldBeFalse();
    }
}
