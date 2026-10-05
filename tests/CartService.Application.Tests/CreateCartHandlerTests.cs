namespace CartService.Application.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using CartService.Application.Tests.Fakes;
using CartService.Domain;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class CreateCartHandlerTests
{
    private readonly HandlerEnvironment _environment = new HandlerEnvironment();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACustomerWithoutACartGetsANewActiveCart()
    {
        Guid customerId = Guid.NewGuid();

        CreateCartResult result = await CreateHandler().HandleAsync(customerId, Token);

        result.Created.ShouldBeTrue();
        result.GuestToken.ShouldBeNull();
        result.Cart.Status.ShouldBe(CartStatus.Active);
        result.Cart.Items.ShouldBeEmpty();
        result.Cart.CreatedAt.ShouldBe(HandlerEnvironment.Start);
        Cart saved = _environment.Carts.SavedCarts.ShouldHaveSingleItem();
        saved.CustomerId.ShouldBe(customerId);
        saved.Id.ShouldBe(result.Cart.Id);
        _environment.UnitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task ACustomerWithAnActiveCartGetsThatCartAndNothingIsCreated()
    {
        Guid customerId = Guid.NewGuid();
        Cart existing = Cart.CreateForCustomer(customerId, HandlerEnvironment.Start);
        _environment.Carts.Seed(existing);

        CreateCartResult result = await CreateHandler().HandleAsync(customerId, Token);

        result.Created.ShouldBeFalse();
        result.Cart.Id.ShouldBe(existing.Id);
        _environment.Carts.SavedCarts.ShouldHaveSingleItem();
        _environment.UnitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task ACartInCheckoutDoesNotCountAsTheActiveCart()
    {
        Guid customerId = Guid.NewGuid();
        Cart inCheckout = Cart.CreateForCustomer(customerId, HandlerEnvironment.Start);
        inCheckout.AddItem("tee", "T-shirt", Money.Eur(10m), 1, HandlerEnvironment.Start);
        inCheckout.Checkout(HandlerEnvironment.Start);
        _environment.Carts.Seed(inCheckout);

        CreateCartResult result = await CreateHandler().HandleAsync(customerId, Token);

        result.Created.ShouldBeTrue();
        result.Cart.Id.ShouldNotBe(inCheckout.Id);
        _environment.Carts.SavedCarts.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenTwoRequestsCreateTheCartAtTheSameMomentTheLoserGetsTheWinnersCart()
    {
        Guid customerId = Guid.NewGuid();
        Cart winner = Cart.CreateForCustomer(customerId, HandlerEnvironment.Start);
        _environment.UnitOfWork.FailWith = new ActiveCartAlreadyExistsException(new InvalidOperationException("duplicate"));
        _environment.UnitOfWork.BeforeFailure = () => _environment.Carts.Seed(winner);

        CreateCartResult result = await CreateHandler().HandleAsync(customerId, Token);

        result.Created.ShouldBeFalse();
        result.Cart.Id.ShouldBe(winner.Id);
        _environment.Carts.SavedCarts.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WithoutACustomerAGuestCartIsCreatedAndTheTokenIsReturnedOnce()
    {
        CreateCartResult result = await CreateHandler().HandleAsync(null, Token);

        result.Created.ShouldBeTrue();
        string guestToken = result.GuestToken.ShouldNotBeNull();
        guestToken.ShouldBe("guest-token-1");
        Cart saved = _environment.Carts.SavedCarts.ShouldHaveSingleItem();
        saved.CustomerId.ShouldBeNull();
        saved.GuestTokenHash.ShouldBe("hash:guest-token-1");
        saved.IsOwnedByGuest(_environment.GuestTokens.Hash(guestToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task EveryGuestCartGetsItsOwnToken()
    {
        CreateCartResult first = await CreateHandler().HandleAsync(null, Token);
        CreateCartResult second = await CreateHandler().HandleAsync(null, Token);

        first.GuestToken.ShouldNotBe(second.GuestToken);
        first.Cart.Id.ShouldNotBe(second.Cart.Id);
    }

    private CreateCartHandler CreateHandler()
    {
        return new CreateCartHandler(_environment.Carts, _environment.UnitOfWork, _environment.GuestTokens, _environment.Time);
    }
}
