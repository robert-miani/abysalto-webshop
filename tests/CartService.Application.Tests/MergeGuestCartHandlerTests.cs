namespace CartService.Application.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using CartService.Application.Tests.Fakes;
using CartService.Domain;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class MergeGuestCartHandlerTests
{
    private readonly HandlerEnvironment _environment = new HandlerEnvironment();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly string _guestHash;

    public MergeGuestCartHandlerTests()
    {
        _guestHash = _environment.GuestTokens.Hash("guest-secret");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheItemsOfTheGuestCartMoveIntoTheCustomerCart()
    {
        Cart customerCart = SeedCustomerCart();
        customerCart.AddItem("tee", "T-shirt", Money.Eur(19.90m), 2, HandlerEnvironment.Start);
        Cart guestCart = SeedGuestCart();
        guestCart.AddItem("tee", "T-shirt", Money.Eur(19.90m), 3, HandlerEnvironment.Start);
        guestCart.AddItem("cap", "Red cap", Money.Eur(9.50m), 1, HandlerEnvironment.Start);

        CartDto dto = await CreateHandler().HandleAsync(_customerId, _guestHash, Token);

        dto.Id.ShouldBe(customerCart.Id);
        dto.Items.Select(item => item.ProductId).ToArray().ShouldBe(new[] { "cap", "tee" });
        dto.Items.Single(item => item.ProductId == "tee").Quantity.ShouldBe(5);
        dto.Total.ShouldBe((5 * 19.90m) + 9.50m);
        guestCart.Status.ShouldBe(CartStatus.Merged);
        _environment.UnitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task ACustomerWithoutAnActiveCartGetsOneThatHoldsTheGuestItems()
    {
        Cart guestCart = SeedGuestCart();
        guestCart.AddItem("cap", "Red cap", Money.Eur(9.50m), 2, HandlerEnvironment.Start);

        CartDto dto = await CreateHandler().HandleAsync(_customerId, _guestHash, Token);

        Cart created = _environment.Carts.SavedCarts.Single(cart => cart.CustomerId == _customerId);
        dto.Id.ShouldBe(created.Id);
        dto.Items.ShouldHaveSingleItem().Quantity.ShouldBe(2);
        guestCart.Status.ShouldBe(CartStatus.Merged);
    }

    [Fact]
    public async Task ACartInCheckoutDoesNotReceiveTheItems()
    {
        Cart inCheckout = SeedCustomerCart();
        inCheckout.AddItem("tee", "T-shirt", Money.Eur(19.90m), 1, HandlerEnvironment.Start);
        inCheckout.Checkout(HandlerEnvironment.Start);
        Cart guestCart = SeedGuestCart();
        guestCart.AddItem("cap", "Red cap", Money.Eur(9.50m), 1, HandlerEnvironment.Start);

        CartDto dto = await CreateHandler().HandleAsync(_customerId, _guestHash, Token);

        dto.Id.ShouldNotBe(inCheckout.Id);
        dto.Items.ShouldHaveSingleItem().ProductId.ShouldBe("cap");
        inCheckout.Items.ShouldHaveSingleItem().ProductId.ShouldBe("tee");
    }

    [Fact]
    public async Task AnEmptyGuestCartIsMergedWithoutChangingTheCustomerCart()
    {
        Cart customerCart = SeedCustomerCart();
        customerCart.AddItem("tee", "T-shirt", Money.Eur(19.90m), 2, HandlerEnvironment.Start);
        int versionBefore = customerCart.Version;
        Cart guestCart = SeedGuestCart();

        CartDto dto = await CreateHandler().HandleAsync(_customerId, _guestHash, Token);

        dto.Items.ShouldHaveSingleItem().Quantity.ShouldBe(2);
        customerCart.Version.ShouldBe(versionBefore);
        guestCart.Status.ShouldBe(CartStatus.Merged);
    }

    [Fact]
    public async Task AnUnknownGuestTokenIsNotFound()
    {
        SeedCustomerCart();

        await Should.ThrowAsync<CartNotFoundException>(
            () => CreateHandler().HandleAsync(_customerId, _environment.GuestTokens.Hash("no-such-token"), Token));

        _environment.UnitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task MergingTheSameGuestCartAgainChangesNothingAndReturnsTheCustomerCart()
    {
        Cart customerCart = SeedCustomerCart();
        customerCart.AddItem("tee", "T-shirt", Money.Eur(19.90m), 2, HandlerEnvironment.Start);
        Cart guestCart = SeedGuestCart();
        guestCart.AddItem("tee", "T-shirt", Money.Eur(19.90m), 3, HandlerEnvironment.Start);
        await CreateHandler().HandleAsync(_customerId, _guestHash, Token);

        CartDto again = await CreateHandler().HandleAsync(_customerId, _guestHash, Token);

        again.Id.ShouldBe(customerCart.Id);
        again.Items.ShouldHaveSingleItem().Quantity.ShouldBe(5);
        _environment.UnitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task RepeatingAMergeWhenTheCustomerHasNoActiveCartAnymoreIsNotFound()
    {
        Cart customerCart = SeedCustomerCart();
        Cart guestCart = SeedGuestCart();
        guestCart.AddItem("tee", "T-shirt", Money.Eur(19.90m), 1, HandlerEnvironment.Start);
        await CreateHandler().HandleAsync(_customerId, _guestHash, Token);
        customerCart.Checkout(HandlerEnvironment.Start);

        await Should.ThrowAsync<CartNotFoundException>(
            () => CreateHandler().HandleAsync(_customerId, _guestHash, Token));
    }

    [Fact]
    public async Task AMergeThatWouldExceedTheProductLimitIsRejectedAndNothingChanges()
    {
        Cart customerCart = SeedCustomerCart();
        Cart guestCart = SeedGuestCart();

        for (int index = 1; index <= 30; index++)
        {
            customerCart.AddItem($"customer-{index}", "Product", Money.Eur(1m), 1, HandlerEnvironment.Start);
            guestCart.AddItem($"guest-{index}", "Product", Money.Eur(1m), 1, HandlerEnvironment.Start);
        }

        CartRuleViolationException error = await Should.ThrowAsync<CartRuleViolationException>(
            () => CreateHandler().HandleAsync(_customerId, _guestHash, Token));

        error.Code.ShouldBe(CartErrorCodes.ItemLimitExceeded);
        customerCart.Items.Count.ShouldBe(30);
        guestCart.Status.ShouldBe(CartStatus.Active);
        _environment.UnitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenAnotherRequestCreatesTheCustomerCartAtTheSameMomentTheErrorIsNotSwallowed()
    {
        Cart guestCart = SeedGuestCart();
        guestCart.AddItem("tee", "T-shirt", Money.Eur(19.90m), 1, HandlerEnvironment.Start);
        _environment.UnitOfWork.FailWith = new ActiveCartAlreadyExistsException(new InvalidOperationException("duplicate"));

        await Should.ThrowAsync<ActiveCartAlreadyExistsException>(
            () => CreateHandler().HandleAsync(_customerId, _guestHash, Token));

        _environment.Carts.SavedCarts.ShouldHaveSingleItem().ShouldBe(guestCart);
    }

    [Fact]
    public async Task AConcurrentChangeOfEitherCartIsReportedAsAConflict()
    {
        SeedCustomerCart();
        Cart guestCart = SeedGuestCart();
        guestCart.AddItem("tee", "T-shirt", Money.Eur(19.90m), 1, HandlerEnvironment.Start);
        _environment.UnitOfWork.FailWith = new ConcurrencyConflictException(new InvalidOperationException("stale"));

        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => CreateHandler().HandleAsync(_customerId, _guestHash, Token));
    }

    private Cart SeedCustomerCart()
    {
        Cart cart = Cart.CreateForCustomer(_customerId, HandlerEnvironment.Start);
        _environment.Carts.Seed(cart);

        return cart;
    }

    private Cart SeedGuestCart()
    {
        Cart cart = Cart.CreateForGuest(_guestHash, HandlerEnvironment.Start);
        _environment.Carts.Seed(cart);

        return cart;
    }

    private MergeGuestCartHandler CreateHandler()
    {
        return new MergeGuestCartHandler(_environment.Carts, _environment.UnitOfWork, _environment.Time, _environment.Cache);
    }
}
