namespace CartService.Domain.Tests;

using System;
using System.Linq;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class CartMergeTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Start.AddMinutes(5);

    private readonly Cart _customerCart = Cart.CreateForCustomer(Guid.NewGuid(), Start);
    private readonly Cart _guestCart = Cart.CreateForGuest("hash", Start);

    [Fact]
    public void ProductsOnlyInTheGuestCartAreAddedWithTheGuestSnapshot()
    {
        _customerCart.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Start);
        _guestCart.AddItem("cap", "Cap", Money.Eur(5m), 2, Start);

        _customerCart.MergeGuestCart(_guestCart, Later);

        _customerCart.Items.Count.ShouldBe(2);
        CartItem cap = _customerCart.Items.Single(item => item.ProductId == "cap");
        cap.ProductName.ShouldBe("Cap");
        cap.UnitPrice.ShouldBe(Money.Eur(5m));
        cap.Quantity.ShouldBe(2);
        _customerCart.Total.ShouldBe(Money.Eur(20m));
    }

    [Fact]
    public void QuantitiesOfTheSameProductAreAddedUp()
    {
        _customerCart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, Start);
        _guestCart.AddItem("tee", "T-shirt", Money.Eur(10m), 3, Start);

        _customerCart.MergeGuestCart(_guestCart, Later);

        _customerCart.Items.ShouldHaveSingleItem().Quantity.ShouldBe(5);
    }

    [Fact]
    public void TheSummedQuantityIsCappedAtTheLimitInsteadOfRejected()
    {
        _customerCart.AddItem("tee", "T-shirt", Money.Eur(10m), 15, Start);
        _guestCart.AddItem("tee", "T-shirt", Money.Eur(10m), 12, Start);

        _customerCart.MergeGuestCart(_guestCart, Later);

        _customerCart.Items.ShouldHaveSingleItem().Quantity.ShouldBe(CartLimits.MaxQuantityPerItem);
        _guestCart.Status.ShouldBe(CartStatus.Merged);
    }

    [Fact]
    public void AnExistingCustomerLineKeepsItsPriceAndName()
    {
        _customerCart.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Start);
        _guestCart.AddItem("tee", "T-shirt (old name)", Money.Eur(8m), 1, Start);

        _customerCart.MergeGuestCart(_guestCart, Later);

        CartItem item = _customerCart.Items.ShouldHaveSingleItem();
        item.UnitPrice.ShouldBe(Money.Eur(10m));
        item.ProductName.ShouldBe("T-shirt");
        item.Quantity.ShouldBe(2);
    }

    [Fact]
    public void TheGuestCartIsMarkedAsMergedAndBothVersionsAdvance()
    {
        _customerCart.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Start);
        _guestCart.AddItem("cap", "Cap", Money.Eur(5m), 1, Start);

        _customerCart.MergeGuestCart(_guestCart, Later);

        _guestCart.Status.ShouldBe(CartStatus.Merged);
        _guestCart.Version.ShouldBe(2);
        _guestCart.UpdatedAt.ShouldBe(Later);
        _customerCart.Status.ShouldBe(CartStatus.Active);
        _customerCart.Version.ShouldBe(2);
        _customerCart.UpdatedAt.ShouldBe(Later);
    }

    [Fact]
    public void MergingAnEmptyGuestCartOnlyMarksTheGuestCart()
    {
        _customerCart.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Start);

        _customerCart.MergeGuestCart(_guestCart, Later);

        _guestCart.Status.ShouldBe(CartStatus.Merged);
        _customerCart.Version.ShouldBe(1);
        _customerCart.UpdatedAt.ShouldBe(Start);
    }

    [Fact]
    public void AMergedGuestCartCannotBeChangedOrMergedAgain()
    {
        _guestCart.AddItem("cap", "Cap", Money.Eur(5m), 1, Start);
        _customerCart.MergeGuestCart(_guestCart, Later);

        Should.Throw<CartRuleViolationException>(() => _guestCart.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Later))
            .Code.ShouldBe(CartErrorCodes.NotActive);
        Should.Throw<CartRuleViolationException>(() => _customerCart.MergeGuestCart(_guestCart, Later))
            .Code.ShouldBe(CartErrorCodes.NotActive);
    }

    [Fact]
    public void ReachingTheDistinctLimitExactlyIsAccepted()
    {
        AddDistinctProducts(_customerCart, "customer", 45);
        AddDistinctProducts(_guestCart, "guest", 5);

        _customerCart.MergeGuestCart(_guestCart, Later);

        _customerCart.Items.Count.ShouldBe(CartLimits.MaxDistinctItems);
    }

    [Fact]
    public void ProductsThatAreInBothCartsDoNotCountTwiceTowardTheDistinctLimit()
    {
        AddDistinctProducts(_customerCart, "shared", CartLimits.MaxDistinctItems);
        AddDistinctProducts(_guestCart, "shared", 3);

        _customerCart.MergeGuestCart(_guestCart, Later);

        _customerCart.Items.Count.ShouldBe(CartLimits.MaxDistinctItems);
        _guestCart.Status.ShouldBe(CartStatus.Merged);
    }

    [Fact]
    public void ExceedingTheDistinctLimitRejectsTheWholeMergeAndChangesNothing()
    {
        AddDistinctProducts(_customerCart, "customer", 45);
        AddDistinctProducts(_guestCart, "guest", 6);
        _customerCart.AddItem("customer-1", "Product", Money.Eur(1m), 1, Start);
        _guestCart.AddItem("customer-1", "Product", Money.Eur(1m), 3, Start);
        int customerVersion = _customerCart.Version;
        int guestVersion = _guestCart.Version;

        CartRuleViolationException error = Should.Throw<CartRuleViolationException>(
            () => _customerCart.MergeGuestCart(_guestCart, Later));

        error.Code.ShouldBe(CartErrorCodes.ItemLimitExceeded);
        _customerCart.Items.Count.ShouldBe(45);
        _customerCart.Items.Single(item => item.ProductId == "customer-1").Quantity.ShouldBe(2);
        _customerCart.Version.ShouldBe(customerVersion);
        _guestCart.Status.ShouldBe(CartStatus.Active);
        _guestCart.Version.ShouldBe(guestVersion);
    }

    [Fact]
    public void OnlyAGuestCartCanBeMerged()
    {
        Cart otherCustomerCart = Cart.CreateForCustomer(Guid.NewGuid(), Start);

        Should.Throw<CartRuleViolationException>(() => _customerCart.MergeGuestCart(otherCustomerCart, Later))
            .Code.ShouldBe(CartErrorCodes.MergeSourceNotGuest);
    }

    [Fact]
    public void ItemsCanOnlyBeMergedIntoACustomerCart()
    {
        Cart otherGuestCart = Cart.CreateForGuest("other-hash", Start);

        Should.Throw<CartRuleViolationException>(() => _guestCart.MergeGuestCart(otherGuestCart, Later))
            .Code.ShouldBe(CartErrorCodes.MergeRequiresCustomerCart);
    }

    [Fact]
    public void ACustomerCartWaitingForCheckoutCannotReceiveAMerge()
    {
        _customerCart.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Start);
        _customerCart.Checkout(Start);
        _guestCart.AddItem("cap", "Cap", Money.Eur(5m), 1, Start);

        Should.Throw<CartRuleViolationException>(() => _customerCart.MergeGuestCart(_guestCart, Later))
            .Code.ShouldBe(CartErrorCodes.NotActive);
        _guestCart.Status.ShouldBe(CartStatus.Active);
    }

    private static void AddDistinctProducts(Cart cart, string prefix, int count)
    {
        for (int index = 1; index <= count; index++)
        {
            cart.AddItem($"{prefix}-{index}", $"Product {index}", Money.Eur(1m), 1, Start);
        }
    }
}
