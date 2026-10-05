namespace CartService.Api.IntegrationTests.Persistence;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CartPersistenceTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;

    public CartPersistenceTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACustomerCartWithLinesSurvivesARoundTrip()
    {
        Guid customerId = Guid.NewGuid();
        Cart cart = Cart.CreateForCustomer(customerId, Now);
        cart.AddItem("tee-blue-m", "Blue T-shirt M", Money.Eur(19.90m), 2, Now.AddMinutes(1));
        cart.AddItem("cap-red", "Red cap", Money.Eur(9.5m), 1, Now.AddMinutes(2));
        await SaveNewAsync(cart);

        Cart loaded = await LoadAsync(cart.Id);

        loaded.CustomerId.ShouldBe(customerId);
        loaded.GuestTokenHash.ShouldBeNull();
        loaded.Status.ShouldBe(CartStatus.Active);
        loaded.CheckoutId.ShouldBeNull();
        loaded.CreatedAt.ShouldBe(Now);
        loaded.UpdatedAt.ShouldBe(Now.AddMinutes(2));
        loaded.Version.ShouldBe(2);
        loaded.Items.Count.ShouldBe(2);
        CartItem tee = loaded.Items.Single(item => item.ProductId == "tee-blue-m");
        tee.ProductName.ShouldBe("Blue T-shirt M");
        tee.UnitPrice.ShouldBe(Money.Eur(19.90m));
        tee.Quantity.ShouldBe(2);
        loaded.Total.ShouldBe(Money.Eur(49.30m));
    }

    [Fact]
    public async Task AGuestCartSurvivesARoundTripAndIsNotACustomersActiveCart()
    {
        Cart cart = Cart.CreateForGuest("guest-token-hash", Now);
        cart.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Now);
        await SaveNewAsync(cart);

        Cart loaded = await LoadAsync(cart.Id);

        loaded.CustomerId.ShouldBeNull();
        loaded.GuestTokenHash.ShouldBe("guest-token-hash");
        loaded.IsOwnedByGuest("guest-token-hash").ShouldBeTrue();
        loaded.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ChangesToTheLinesArePersisted()
    {
        Cart cart = Cart.CreateForCustomer(Guid.NewGuid(), Now);
        cart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, Now);
        cart.AddItem("cap", "Cap", Money.Eur(5m), 1, Now);
        await SaveNewAsync(cart);

        await using (CartDbContext context = _postgres.CreateContext())
        {
            Cart tracked = (await new CartRepository(context).GetByIdAsync(cart.Id, Token))!;
            tracked.ChangeQuantity("tee", 7, Now.AddMinutes(5));
            tracked.RemoveItem("cap", Now.AddMinutes(5));
            tracked.AddItem("belt", "Belt", Money.Eur(15m), 1, Now.AddMinutes(5));
            await new UnitOfWork(context).SaveChangesAsync(Token);
        }

        Cart loaded = await LoadAsync(cart.Id);

        loaded.Items.Select(item => item.ProductId).Order().ShouldBe(new[] { "belt", "tee" });
        loaded.Items.Single(item => item.ProductId == "tee").Quantity.ShouldBe(7);
        loaded.Version.ShouldBe(5);
        loaded.UpdatedAt.ShouldBe(Now.AddMinutes(5));
    }

    [Fact]
    public async Task AnUnknownCartIsNotFound()
    {
        await using CartDbContext context = _postgres.CreateContext();

        Cart? cart = await new CartRepository(context).GetByIdAsync(Guid.NewGuid(), Token);

        cart.ShouldBeNull();
    }

    [Fact]
    public async Task AGuestCartIsFoundByTheHashOfItsToken()
    {
        string hash = $"hash-{Guid.NewGuid():N}";
        Cart cart = Cart.CreateForGuest(hash, Now);
        cart.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Now);
        await SaveNewAsync(cart);

        await using CartDbContext context = _postgres.CreateContext();
        CartRepository repository = new CartRepository(context);

        Cart? found = await repository.GetByGuestTokenHashAsync(hash, Token);
        Cart? unknown = await repository.GetByGuestTokenHashAsync("no-such-hash", Token);

        found.ShouldNotBeNull();
        found.Id.ShouldBe(cart.Id);
        found.Items.ShouldHaveSingleItem();
        unknown.ShouldBeNull();
    }

    [Fact]
    public async Task TwoCartsCannotShareTheSameGuestToken()
    {
        string hash = $"hash-{Guid.NewGuid():N}";
        await SaveNewAsync(Cart.CreateForGuest(hash, Now));

        DbUpdateException error = await Should.ThrowAsync<DbUpdateException>(
            () => SaveNewAsync(Cart.CreateForGuest(hash, Now)));

        PostgresException postgresError = error.InnerException.ShouldBeOfType<PostgresException>();
        postgresError.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgresError.ConstraintName.ShouldBe("ux_carts_guest_token_hash");
    }

    [Fact]
    public async Task TheActiveCartOfACustomerIsFoundByCustomerId()
    {
        Guid customerId = Guid.NewGuid();
        Cart cart = Cart.CreateForCustomer(customerId, Now);
        await SaveNewAsync(cart);
        await SaveNewAsync(Cart.CreateForGuest("another-guest", Now));

        await using CartDbContext context = _postgres.CreateContext();
        CartRepository repository = new CartRepository(context);

        (await repository.GetActiveByCustomerAsync(customerId, Token))!.Id.ShouldBe(cart.Id);
        (await repository.GetActiveByCustomerAsync(Guid.NewGuid(), Token)).ShouldBeNull();
    }

    [Fact]
    public async Task TwoRequestsChangingTheSameCartCauseAConcurrencyConflict()
    {
        Cart cart = Cart.CreateForCustomer(Guid.NewGuid(), Now);
        cart.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Now);
        await SaveNewAsync(cart);

        await using CartDbContext first = _postgres.CreateContext();
        await using CartDbContext second = _postgres.CreateContext();
        Cart cartOfFirst = (await new CartRepository(first).GetByIdAsync(cart.Id, Token))!;
        Cart cartOfSecond = (await new CartRepository(second).GetByIdAsync(cart.Id, Token))!;

        cartOfFirst.AddItem("cap", "Cap", Money.Eur(5m), 1, Now.AddMinutes(1));
        await new UnitOfWork(first).SaveChangesAsync(Token);

        cartOfSecond.ChangeQuantity("tee", 9, Now.AddMinutes(2));
        await Should.ThrowAsync<ConcurrencyConflictException>(() => new UnitOfWork(second).SaveChangesAsync(Token));

        Cart loaded = await LoadAsync(cart.Id);
        loaded.Items.Count.ShouldBe(2);
        loaded.Items.Single(item => item.ProductId == "tee").Quantity.ShouldBe(1);
    }

    [Fact]
    public async Task ASecondActiveCartForTheSameCustomerIsRejected()
    {
        Guid customerId = Guid.NewGuid();
        await SaveNewAsync(Cart.CreateForCustomer(customerId, Now));

        await Should.ThrowAsync<ActiveCartAlreadyExistsException>(
            () => SaveNewAsync(Cart.CreateForCustomer(customerId, Now)));
    }

    [Fact]
    public async Task ACartInCheckoutDoesNotBlockANewActiveCart()
    {
        Guid customerId = Guid.NewGuid();
        Cart first = Cart.CreateForCustomer(customerId, Now);
        first.AddItem("tee", "T-shirt", Money.Eur(10m), 1, Now);
        Guid checkoutId = first.Checkout(Now.AddMinutes(1));
        await SaveNewAsync(first);

        Cart second = Cart.CreateForCustomer(customerId, Now.AddMinutes(2));
        await SaveNewAsync(second);

        await using CartDbContext context = _postgres.CreateContext();
        (await new CartRepository(context).GetActiveByCustomerAsync(customerId, Token))!.Id.ShouldBe(second.Id);
        Cart pending = await LoadAsync(first.Id);
        pending.Status.ShouldBe(CartStatus.CheckoutPending);
        pending.CheckoutId.ShouldBe(checkoutId);
    }

    [Fact]
    public async Task MergingAGuestCartIsPersistedForBothCarts()
    {
        Cart customerCart = Cart.CreateForCustomer(Guid.NewGuid(), Now);
        customerCart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, Now);
        Cart guestCart = Cart.CreateForGuest("guest-hash", Now);
        guestCart.AddItem("tee", "T-shirt", Money.Eur(10m), 3, Now);
        guestCart.AddItem("cap", "Cap", Money.Eur(5m), 1, Now);
        await SaveNewAsync(customerCart);
        await SaveNewAsync(guestCart);

        await using (CartDbContext context = _postgres.CreateContext())
        {
            CartRepository repository = new CartRepository(context);
            Cart customer = (await repository.GetByIdAsync(customerCart.Id, Token))!;
            Cart guest = (await repository.GetByIdAsync(guestCart.Id, Token))!;
            customer.MergeGuestCart(guest, Now.AddMinutes(10));
            await new UnitOfWork(context).SaveChangesAsync(Token);
        }

        Cart mergedCustomer = await LoadAsync(customerCart.Id);
        Cart mergedGuest = await LoadAsync(guestCart.Id);
        mergedCustomer.Items.Single(item => item.ProductId == "tee").Quantity.ShouldBe(5);
        mergedCustomer.Items.Single(item => item.ProductId == "cap").Quantity.ShouldBe(1);
        mergedGuest.Status.ShouldBe(CartStatus.Merged);
    }

    [Fact]
    public async Task TheDatabaseRejectsACartWithoutAnOwner()
    {
        await using CartDbContext context = _postgres.CreateContext();

        PostgresException error = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO carts (id, customer_id, guest_token_hash, status, created_at, updated_at, version) VALUES ({Guid.NewGuid()}, NULL, NULL, 'Active', now(), now(), 0)",
            Token));

        error.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        error.ConstraintName.ShouldBe("ck_carts_single_owner");
    }

    [Fact]
    public async Task TheDatabaseRejectsAQuantityOutsideTheLimits()
    {
        Cart cart = Cart.CreateForCustomer(Guid.NewGuid(), Now);
        await SaveNewAsync(cart);
        await using CartDbContext context = _postgres.CreateContext();

        PostgresException error = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO cart_items (cart_id, product_id, product_name, unit_price, currency, quantity) VALUES ({cart.Id}, 'tee', 'T-shirt', 10.00, 'EUR', 0)",
            Token));

        error.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        error.ConstraintName.ShouldBe("ck_cart_items_quantity");
    }

    private async Task SaveNewAsync(Cart cart)
    {
        await using CartDbContext context = _postgres.CreateContext();
        new CartRepository(context).Add(cart);
        await new UnitOfWork(context).SaveChangesAsync(Token);
    }

    private async Task<Cart> LoadAsync(Guid id)
    {
        await using CartDbContext context = _postgres.CreateContext();
        Cart? cart = await new CartRepository(context).GetByIdAsync(id, Token);

        return cart ?? throw new InvalidOperationException($"Cart {id} was not found.");
    }
}
