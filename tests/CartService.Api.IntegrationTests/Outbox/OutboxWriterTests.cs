namespace CartService.Api.IntegrationTests.Outbox;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.IntegrationTests.Persistence;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using CartService.Domain;
using CartService.Infrastructure.Caching;
using CartService.Infrastructure.Outbox;
using CartService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxWriterTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;

    public OutboxWriterTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CheckoutStoresACloudEventTogetherWithTheCartChange()
    {
        (Guid customerId, Cart cart) = await SeedCartAsync();

        CheckoutResult result = await CheckoutAsync(cart.Id, customerId);

        OutboxMessage message = (await LoadMessagesAsync(result.CheckoutId)).ShouldHaveSingleItem();
        message.Type.ShouldBe("CartCheckedOut");
        message.OrderingKey.ShouldBe(result.CheckoutId.ToString());
        message.ProcessedAt.ShouldBeNull();
        message.LockedUntil.ShouldBeNull();
        message.Attempts.ShouldBe(0);
        Cart saved = await LoadCartAsync(cart.Id);
        saved.Status.ShouldBe(CartStatus.CheckoutPending);
        saved.CheckoutId.ShouldBe(result.CheckoutId);
    }

    [Fact]
    public async Task TheStoredPayloadIsACloudEventWithTheCartContentAsData()
    {
        (Guid customerId, Cart cart) = await SeedCartAsync();

        CheckoutResult result = await CheckoutAsync(cart.Id, customerId);

        OutboxMessage message = (await LoadMessagesAsync(result.CheckoutId)).Single();
        using JsonDocument document = JsonDocument.Parse(message.Payload);
        JsonElement envelope = document.RootElement;
        envelope.GetProperty("specversion").GetString().ShouldBe("1.0");
        envelope.GetProperty("id").GetGuid().ShouldBe(message.Id);
        envelope.GetProperty("source").GetString().ShouldBe("/services/cart");
        envelope.GetProperty("type").GetString().ShouldBe("CartCheckedOut");
        envelope.GetProperty("subject").GetString().ShouldBe(result.CheckoutId.ToString());
        envelope.GetProperty("datacontenttype").GetString().ShouldBe("application/json");
        envelope.GetProperty("time").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));

        JsonElement data = envelope.GetProperty("data");
        data.GetProperty("checkoutId").GetGuid().ShouldBe(result.CheckoutId);
        data.GetProperty("cartId").GetGuid().ShouldBe(cart.Id);
        data.GetProperty("customerId").GetGuid().ShouldBe(customerId);
        data.GetProperty("currency").GetString().ShouldBe("EUR");
        data.GetProperty("indicativeTotal").GetDecimal().ShouldBe(49.30m);
        JsonElement[] items = data.GetProperty("items").EnumerateArray().ToArray();
        items.Length.ShouldBe(2);
        items[0].GetProperty("productId").GetString().ShouldBe("cap");
        items[1].GetProperty("productId").GetString().ShouldBe("tee");
        items[1].GetProperty("quantity").GetInt32().ShouldBe(2);
        items[1].GetProperty("unitPrice").GetDecimal().ShouldBe(19.90m);

        // The type and the ordering key are in the envelope and must not be repeated in the data.
        data.TryGetProperty("type", out _).ShouldBeFalse();
        data.TryGetProperty("orderingKey", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenTheCartWasChangedMeanwhileTheCheckoutIsRejectedAndNoEventIsStored()
    {
        (Guid customerId, Cart cart) = await SeedCartAsync();
        await using CartDbContext context = _postgres.CreateContext();
        HookedCartRepository repository = new HookedCartRepository(new CartRepository(context), async () =>
        {
            // Another request adds an item after this request has read the cart.
            await using CartDbContext other = _postgres.CreateContext();
            Cart sameCart = (await new CartRepository(other).GetByIdAsync(cart.Id, Token))!;
            sameCart.AddItem("mug", "Mug", Money.Eur(6.50m), 1, Now.AddMinutes(1));
            await new UnitOfWork(other).SaveChangesAsync(Token);
        });
        CheckoutHandler handler = new CheckoutHandler(repository, new UnitOfWork(context), new OutboxWriter(context), TimeProvider.System, new NullCartCache());

        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => handler.HandleAsync(cart.Id, Requester.ForCustomer(customerId), Token));

        await using CartDbContext verification = _postgres.CreateContext();
        string cartIdPattern = $"%{cart.Id}%";
        int messagesOfThisCart = await verification.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM outbox_messages WHERE payload::text LIKE {cartIdPattern}")
            .SingleAsync(Token);
        messagesOfThisCart.ShouldBe(0);
        Cart saved = await LoadCartAsync(cart.Id);
        saved.Status.ShouldBe(CartStatus.Active);
        saved.Items.Count.ShouldBe(3);
    }

    [Fact]
    public async Task RepeatingTheCheckoutDoesNotStoreASecondEvent()
    {
        (Guid customerId, Cart cart) = await SeedCartAsync();

        CheckoutResult first = await CheckoutAsync(cart.Id, customerId);
        CheckoutResult second = await CheckoutAsync(cart.Id, customerId);

        second.CheckoutId.ShouldBe(first.CheckoutId);
        (await LoadMessagesAsync(first.CheckoutId)).ShouldHaveSingleItem();
    }

    private async Task<(Guid CustomerId, Cart Cart)> SeedCartAsync()
    {
        Guid customerId = Guid.NewGuid();
        Cart cart = Cart.CreateForCustomer(customerId, Now);
        cart.AddItem("tee", "T-shirt", Money.Eur(19.90m), 2, Now);
        cart.AddItem("cap", "Red cap", Money.Eur(9.50m), 1, Now);

        await using CartDbContext context = _postgres.CreateContext();
        new CartRepository(context).Add(cart);
        await new UnitOfWork(context).SaveChangesAsync(Token);

        return (customerId, cart);
    }

    private async Task<CheckoutResult> CheckoutAsync(Guid cartId, Guid customerId)
    {
        await using CartDbContext context = _postgres.CreateContext();
        CheckoutHandler handler = new CheckoutHandler(new CartRepository(context), new UnitOfWork(context), new OutboxWriter(context), TimeProvider.System, new NullCartCache());

        return await handler.HandleAsync(cartId, Requester.ForCustomer(customerId), Token);
    }

    private async Task<List<OutboxMessage>> LoadMessagesAsync(Guid checkoutId)
    {
        await using CartDbContext context = _postgres.CreateContext();

        return await context.OutboxMessages.AsNoTracking()
            .Where(message => message.OrderingKey == checkoutId.ToString())
            .ToListAsync(Token);
    }

    private async Task<Cart> LoadCartAsync(Guid cartId)
    {
        await using CartDbContext context = _postgres.CreateContext();
        Cart? cart = await new CartRepository(context).GetByIdAsync(cartId, Token);

        return cart ?? throw new InvalidOperationException($"Cart {cartId} was not found.");
    }
}
