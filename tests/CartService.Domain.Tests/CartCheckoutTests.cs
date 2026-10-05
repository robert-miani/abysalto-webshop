namespace CartService.Domain.Tests;

using System;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class CartCheckoutTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Start.AddMinutes(5);

    private readonly Cart _cart = Cart.CreateForCustomer(Guid.NewGuid(), Start);

    [Fact]
    public void CheckoutMovesTheCartToCheckoutPendingAndReturnsTheCheckoutId()
    {
        _cart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, Start);

        Guid checkoutId = _cart.Checkout(Later);

        checkoutId.ShouldNotBe(Guid.Empty);
        _cart.CheckoutId.ShouldBe(checkoutId);
        _cart.Status.ShouldBe(CartStatus.CheckoutPending);
        _cart.Version.ShouldBe(2);
        _cart.UpdatedAt.ShouldBe(Later);
        _cart.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public void AnEmptyCartCannotBeCheckedOut()
    {
        CartRuleViolationException error = Should.Throw<CartRuleViolationException>(() => _cart.Checkout(Later));

        error.Code.ShouldBe(CartErrorCodes.Empty);
        _cart.Status.ShouldBe(CartStatus.Active);
        _cart.CheckoutId.ShouldBeNull();
    }

    [Fact]
    public void AGuestCartCannotBeCheckedOut()
    {
        Cart guestCart = Cart.CreateForGuest("hash", Start);
        guestCart.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Start);

        CartRuleViolationException error = Should.Throw<CartRuleViolationException>(() => guestCart.Checkout(Later));

        error.Code.ShouldBe(CartErrorCodes.CheckoutRequiresCustomer);
        guestCart.Status.ShouldBe(CartStatus.Active);
    }

    [Fact]
    public void ACartThatIsAlreadyCheckedOutCannotBeCheckedOutAgain()
    {
        _cart.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Start);
        Guid checkoutId = _cart.Checkout(Later);

        CartRuleViolationException error = Should.Throw<CartRuleViolationException>(() => _cart.Checkout(Later.AddMinutes(1)));

        error.Code.ShouldBe(CartErrorCodes.NotActive);
        _cart.CheckoutId.ShouldBe(checkoutId);
        _cart.Version.ShouldBe(2);
    }

    [Fact]
    public void ACartWaitingForCheckoutCannotBeChanged()
    {
        _cart.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Start);
        _cart.Checkout(Later);

        Should.Throw<CartRuleViolationException>(() => _cart.AddItem("cap", "Cap", Money.Eur(5m), 1, Later))
            .Code.ShouldBe(CartErrorCodes.NotActive);
        Should.Throw<CartRuleViolationException>(() => _cart.ChangeQuantity("tee", 2, Later))
            .Code.ShouldBe(CartErrorCodes.NotActive);
        Should.Throw<CartRuleViolationException>(() => _cart.RemoveItem("tee", Later))
            .Code.ShouldBe(CartErrorCodes.NotActive);

        _cart.Items.ShouldHaveSingleItem().Quantity.ShouldBe(1);
        _cart.Version.ShouldBe(2);
    }
}
