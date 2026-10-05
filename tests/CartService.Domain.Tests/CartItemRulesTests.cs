namespace CartService.Domain.Tests;

using System;
using System.Linq;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class CartItemRulesTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Start.AddMinutes(5);

    private readonly Cart _cart = Cart.CreateForCustomer(Guid.NewGuid(), Start);

    [Fact]
    public void AddItemCreatesALineAndUpdatesTotalVersionAndTimestamp()
    {
        _cart.AddItem("tee-blue-m", "Blue T-shirt M", Money.Eur(19.90m), 2, Later);

        CartItem item = _cart.Items.ShouldHaveSingleItem();
        item.ProductId.ShouldBe("tee-blue-m");
        item.ProductName.ShouldBe("Blue T-shirt M");
        item.UnitPrice.ShouldBe(Money.Eur(19.90m));
        item.Quantity.ShouldBe(2);
        item.LineTotal.ShouldBe(Money.Eur(39.80m));
        _cart.Total.ShouldBe(Money.Eur(39.80m));
        _cart.Version.ShouldBe(1);
        _cart.UpdatedAt.ShouldBe(Later);
        _cart.CreatedAt.ShouldBe(Start);
    }

    [Fact]
    public void AddingAnExistingProductAddsTheQuantitiesAndTakesTheLatestPriceAndName()
    {
        _cart.AddItem("tee-blue-m", "Blue T-shirt M", Money.Eur(19.90m), 2, Start);

        _cart.AddItem("tee-blue-m", "Blue T-shirt (M)", Money.Eur(17.90m), 3, Later);

        CartItem item = _cart.Items.ShouldHaveSingleItem();
        item.Quantity.ShouldBe(5);
        item.UnitPrice.ShouldBe(Money.Eur(17.90m));
        item.ProductName.ShouldBe("Blue T-shirt (M)");
        _cart.Total.ShouldBe(Money.Eur(89.50m));
        _cart.Version.ShouldBe(2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void QuantitiesAtTheLimitsAreAccepted(int quantity)
    {
        _cart.AddItem("tee", "T-shirt", Money.Eur(1m), quantity, Later);

        _cart.Items.ShouldHaveSingleItem().Quantity.ShouldBe(quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(21)]
    public void QuantitiesOutsideTheLimitsAreRejected(int quantity)
    {
        CartRuleViolationException error = Should.Throw<CartRuleViolationException>(
            () => _cart.AddItem("tee", "T-shirt", Money.Eur(1m), quantity, Later));

        error.Code.ShouldBe(CartErrorCodes.QuantityOutOfRange);
        _cart.Items.ShouldBeEmpty();
        _cart.Version.ShouldBe(0);
    }

    [Fact]
    public void AddingMoreThanTheLineLimitIsRejectedAndLeavesTheLineUnchanged()
    {
        _cart.AddItem("tee", "T-shirt", Money.Eur(1m), 15, Start);

        CartRuleViolationException error = Should.Throw<CartRuleViolationException>(
            () => _cart.AddItem("tee", "T-shirt", Money.Eur(2m), 6, Later));

        error.Code.ShouldBe(CartErrorCodes.QuantityOutOfRange);
        CartItem item = _cart.Items.ShouldHaveSingleItem();
        item.Quantity.ShouldBe(15);
        item.UnitPrice.ShouldBe(Money.Eur(1m));
        _cart.Version.ShouldBe(1);
        _cart.UpdatedAt.ShouldBe(Start);
    }

    [Fact]
    public void ADifferentProductBeyondTheDistinctLimitIsRejected()
    {
        FillWithDistinctProducts(CartLimits.MaxDistinctItems);

        CartRuleViolationException error = Should.Throw<CartRuleViolationException>(
            () => _cart.AddItem("one-too-many", "Extra", Money.Eur(1m), 1, Later));

        error.Code.ShouldBe(CartErrorCodes.ItemLimitExceeded);
        _cart.Items.Count.ShouldBe(CartLimits.MaxDistinctItems);
    }

    [Fact]
    public void AnExistingProductCanStillBeAddedWhenTheDistinctLimitIsReached()
    {
        FillWithDistinctProducts(CartLimits.MaxDistinctItems);

        _cart.AddItem("product-1", "Product 1", Money.Eur(1m), 1, Later);

        _cart.Items.Count.ShouldBe(CartLimits.MaxDistinctItems);
        _cart.Items.First(item => item.ProductId == "product-1").Quantity.ShouldBe(2);
    }

    [Fact]
    public void APriceInAnotherCurrencyIsRejected()
    {
        Should.Throw<ArgumentException>(
            () => _cart.AddItem("tee", "T-shirt", Money.Create(1m, "USD"), 1, Later));
    }

    [Theory]
    [InlineData("", "T-shirt")]
    [InlineData("tee", " ")]
    public void ABlankProductIdOrNameIsRejected(string productId, string productName)
    {
        Should.Throw<ArgumentException>(
            () => _cart.AddItem(productId, productName, Money.Eur(1m), 1, Later));
    }

    [Fact]
    public void ChangeQuantitySetsTheNewQuantity()
    {
        _cart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, Start);

        _cart.ChangeQuantity("tee", 7, Later);

        _cart.Items.ShouldHaveSingleItem().Quantity.ShouldBe(7);
        _cart.Total.ShouldBe(Money.Eur(70m));
        _cart.Version.ShouldBe(2);
        _cart.UpdatedAt.ShouldBe(Later);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void ChangeQuantityRejectsQuantitiesOutsideTheLimits(int quantity)
    {
        _cart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, Start);

        CartRuleViolationException error = Should.Throw<CartRuleViolationException>(
            () => _cart.ChangeQuantity("tee", quantity, Later));

        error.Code.ShouldBe(CartErrorCodes.QuantityOutOfRange);
        _cart.Items.ShouldHaveSingleItem().Quantity.ShouldBe(2);
    }

    [Fact]
    public void ChangeQuantityOfAnUnknownProductIsRejected()
    {
        CartRuleViolationException error = Should.Throw<CartRuleViolationException>(
            () => _cart.ChangeQuantity("missing", 1, Later));

        error.Code.ShouldBe(CartErrorCodes.ItemNotFound);
    }

    [Fact]
    public void RemoveItemDeletesTheLine()
    {
        _cart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, Start);
        _cart.AddItem("cap", "Cap", Money.Eur(5m), 1, Start);

        _cart.RemoveItem("tee", Later);

        _cart.Items.ShouldHaveSingleItem().ProductId.ShouldBe("cap");
        _cart.Total.ShouldBe(Money.Eur(5m));
        _cart.Version.ShouldBe(3);
    }

    [Fact]
    public void RemovingAProductThatIsNotInTheCartChangesNothing()
    {
        _cart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, Start);

        _cart.RemoveItem("missing", Later);

        _cart.Items.ShouldHaveSingleItem();
        _cart.Version.ShouldBe(1);
        _cart.UpdatedAt.ShouldBe(Start);
    }

    [Fact]
    public void GuestCartsFollowTheSameRules()
    {
        Cart guestCart = Cart.CreateForGuest("hash", Start);

        guestCart.AddItem("tee", "T-shirt", Money.Eur(10m), 20, Later);

        Should.Throw<CartRuleViolationException>(() => guestCart.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Later));
    }

    private void FillWithDistinctProducts(int count)
    {
        for (int index = 1; index <= count; index++)
        {
            _cart.AddItem($"product-{index}", $"Product {index}", Money.Eur(1m), 1, Start);
        }
    }
}
