namespace CartService.Application.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Carts;
using CartService.Application.Tests.Fakes;
using CartService.Domain;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ItemHandlerTests
{
    private readonly HandlerEnvironment _environment = new HandlerEnvironment();
    private readonly Guid _customerId = Guid.NewGuid();

    public ItemHandlerTests()
    {
        _environment.Catalog.Add("tee", "T-shirt", 10m);
        _environment.Catalog.Add("cap", "Cap", 5m);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Requester Customer => Requester.ForCustomer(_customerId);

    [Fact]
    public async Task AddingAProductUsesTheNameAndPriceFromTheCatalog()
    {
        Cart cart = SeedCart();

        CartDto dto = await CreateAddHandler().HandleAsync(cart.Id, Customer, "tee", 2, Token);

        CartItemDto item = dto.Items.ShouldHaveSingleItem();
        item.ProductId.ShouldBe("tee");
        item.ProductName.ShouldBe("T-shirt");
        item.UnitPrice.ShouldBe(10m);
        item.Quantity.ShouldBe(2);
        dto.Total.ShouldBe(20m);
        _environment.UnitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task AddingTheSameProductAgainAddsToTheQuantityAtTheCurrentPrice()
    {
        Cart cart = SeedCart();
        await CreateAddHandler().HandleAsync(cart.Id, Customer, "tee", 2, Token);
        _environment.Catalog.Add("tee", "T-shirt", 12m);

        CartDto dto = await CreateAddHandler().HandleAsync(cart.Id, Customer, "tee", 3, Token);

        CartItemDto item = dto.Items.ShouldHaveSingleItem();
        item.Quantity.ShouldBe(5);
        item.UnitPrice.ShouldBe(12m);
    }

    [Fact]
    public async Task AnUnknownProductIsRejectedAndNothingIsSaved()
    {
        Cart cart = SeedCart();

        CartRuleViolationException error = await Should.ThrowAsync<CartRuleViolationException>(
            () => CreateAddHandler().HandleAsync(cart.Id, Customer, "does-not-exist", 1, Token));

        error.Code.ShouldBe(CartErrorCodes.ProductNotFound);
        cart.Items.ShouldBeEmpty();
        _environment.UnitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task AQuantityAboveTheLimitIsRejectedAndNothingIsSaved()
    {
        Cart cart = SeedCart();

        CartRuleViolationException error = await Should.ThrowAsync<CartRuleViolationException>(
            () => CreateAddHandler().HandleAsync(cart.Id, Customer, "tee", CartLimits.MaxQuantityPerItem + 1, Token));

        error.Code.ShouldBe(CartErrorCodes.QuantityOutOfRange);
        _environment.UnitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task ACartInCheckoutCannotBeChanged()
    {
        Cart cart = SeedCart();
        cart.AddItem("cap", "Cap", Money.Eur(5m), 1, HandlerEnvironment.Start);
        cart.Checkout(HandlerEnvironment.Start);

        CartRuleViolationException error = await Should.ThrowAsync<CartRuleViolationException>(
            () => CreateAddHandler().HandleAsync(cart.Id, Customer, "tee", 1, Token));

        error.Code.ShouldBe(CartErrorCodes.NotActive);
    }

    [Fact]
    public async Task AnotherCustomerCannotAddToTheCart()
    {
        Cart cart = SeedCart();

        await Should.ThrowAsync<CartNotFoundException>(
            () => CreateAddHandler().HandleAsync(cart.Id, Requester.ForCustomer(Guid.NewGuid()), "tee", 1, Token));

        cart.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task AGuestWithTheRightTokenCanAddToTheGuestCart()
    {
        string hash = _environment.GuestTokens.Hash("secret");
        Cart cart = Cart.CreateForGuest(hash, HandlerEnvironment.Start);
        _environment.Carts.Seed(cart);

        CartDto dto = await CreateAddHandler().HandleAsync(cart.Id, Requester.ForGuest(hash), "cap", 1, Token);

        dto.Items.ShouldHaveSingleItem().ProductId.ShouldBe("cap");
    }

    [Fact]
    public async Task ChangingTheQuantitySetsTheNewQuantity()
    {
        Cart cart = SeedCart();
        cart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, HandlerEnvironment.Start);

        CartDto dto = await CreateChangeHandler().HandleAsync(cart.Id, Customer, "tee", 7, Token);

        dto.Items.ShouldHaveSingleItem().Quantity.ShouldBe(7);
        dto.Total.ShouldBe(70m);
        _environment.UnitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task ChangingTheQuantityOfAProductThatIsNotInTheCartIsRejected()
    {
        Cart cart = SeedCart();

        CartRuleViolationException error = await Should.ThrowAsync<CartRuleViolationException>(
            () => CreateChangeHandler().HandleAsync(cart.Id, Customer, "tee", 1, Token));

        error.Code.ShouldBe(CartErrorCodes.ItemNotFound);
    }

    [Fact]
    public async Task AnotherCustomerCannotChangeTheQuantity()
    {
        Cart cart = SeedCart();
        cart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, HandlerEnvironment.Start);

        await Should.ThrowAsync<CartNotFoundException>(
            () => CreateChangeHandler().HandleAsync(cart.Id, Requester.ForCustomer(Guid.NewGuid()), "tee", 9, Token));

        cart.Items.Single().Quantity.ShouldBe(2);
    }

    [Fact]
    public async Task RemovingAProductDeletesItsLine()
    {
        Cart cart = SeedCart();
        cart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, HandlerEnvironment.Start);
        cart.AddItem("cap", "Cap", Money.Eur(5m), 1, HandlerEnvironment.Start);

        await CreateRemoveHandler().HandleAsync(cart.Id, Customer, "tee", Token);

        cart.Items.ShouldHaveSingleItem().ProductId.ShouldBe("cap");
        _environment.UnitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task RemovingAProductThatIsNotInTheCartIsNotAnError()
    {
        Cart cart = SeedCart();
        cart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, HandlerEnvironment.Start);

        await CreateRemoveHandler().HandleAsync(cart.Id, Customer, "cap", Token);

        cart.Items.ShouldHaveSingleItem().ProductId.ShouldBe("tee");
    }

    [Fact]
    public async Task AnotherCustomerCannotRemoveAProduct()
    {
        Cart cart = SeedCart();
        cart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, HandlerEnvironment.Start);

        await Should.ThrowAsync<CartNotFoundException>(
            () => CreateRemoveHandler().HandleAsync(cart.Id, Requester.ForCustomer(Guid.NewGuid()), "tee", Token));

        cart.Items.ShouldHaveSingleItem();
    }

    private Cart SeedCart()
    {
        Cart cart = Cart.CreateForCustomer(_customerId, HandlerEnvironment.Start);
        _environment.Carts.Seed(cart);

        return cart;
    }

    private AddItemHandler CreateAddHandler()
    {
        return new AddItemHandler(_environment.Carts, _environment.UnitOfWork, _environment.Catalog, _environment.Time);
    }

    private ChangeItemQuantityHandler CreateChangeHandler()
    {
        return new ChangeItemQuantityHandler(_environment.Carts, _environment.UnitOfWork, _environment.Time);
    }

    private RemoveItemHandler CreateRemoveHandler()
    {
        return new RemoveItemHandler(_environment.Carts, _environment.UnitOfWork, _environment.Time);
    }
}
