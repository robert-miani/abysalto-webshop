namespace CartService.Api.Carts;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.Authentication;
using CartService.Api.Idempotency;
using CartService.Api.RateLimiting;
using CartService.Api.Requesters;
using CartService.Application.Carts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

internal static class CartEndpoints
{
    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder carts = endpoints.MapGroup("/v1/carts")
            .WithTags("Carts")
            .RequireRateLimiting(RateLimitingRegistration.DefaultPolicy)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // A cart is personal data and must never be stored by a cache.
        carts.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";

            return await next(context);
        });

        carts.MapPost("/", CreateCart)
            .WithName("CreateCart")
            .WithSummary("Creates a cart. A customer gets their active cart; without a token a guest cart is created.")
            .WithDescription("A signed-in customer gets their one active cart: 201 with the cart when it was created, 200 when it already existed. "
                + "Without a token, a guest cart is created and 201 returns the cart together with its secret guestToken, which is shown only once. "
                + "A bearer token that is sent but not valid (expired, wrong audience, bad signature) answers 401 and creates nothing, so a "
                + "signed-in customer whose token expired is never given an unrelated guest cart by mistake.")
            .Produces<GuestCartCreatedResponse>(StatusCodes.Status201Created)
            .Produces<CartDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting(RateLimitingRegistration.StrictPolicy)
            .AllowAnonymous();

        carts.MapGet("/me", GetMyCart)
            .WithName("GetMyCart")
            .WithSummary("Returns the active cart of the signed-in customer.")
            .Produces<CartDto>(StatusCodes.Status200OK)
            .ProducesProblems(StatusCodes.Status401Unauthorized, StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthenticationRegistration.CustomerPolicy)
            .AddEndpointFilter<RequireRequesterFilter>();

        carts.MapPost("/me/merge", MergeGuestCart)
            .WithName("MergeGuestCart")
            .WithSummary("Merges a guest cart into the cart of the signed-in customer.")
            .WithDescription("Send the bearer token of the customer and, in the X-Cart-Token header, the secret token of the guest cart. "
                + "Quantities of the same product add up (at most 20), the customer's name and price win for such a product, and the "
                + "guest cart becomes read-only. If the result would hold more than 50 products, nothing changes and the answer is 422. "
                + "Repeating the request changes nothing and returns the customer's cart.")
            .Produces<CartDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblems(
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(AuthenticationRegistration.CustomerPolicy)
            .AddEndpointFilter<RequireRequesterFilter>();

        carts.MapGet("/{cartId:guid}", GetCart)
            .WithName("GetCart")
            .WithSummary("Returns a cart of the customer or the guest.")
            .WithDescription("A cart that does not exist and a cart of somebody else both answer 404, so nobody can find out which cart ids exist.")
            .Produces<CartDto>(StatusCodes.Status200OK)
            .ProducesProblems(StatusCodes.Status401Unauthorized, StatusCodes.Status404NotFound)
            .AddEndpointFilter<RequireRequesterFilter>();

        carts.MapPost("/{cartId:guid}/items", AddItem)
            .WithName("AddItem")
            .WithSummary("Adds a product to the cart. The price comes from the catalog.")
            .WithDescription("Adding a product that is already in the cart adds the quantities and takes the latest catalog name and price. "
                + "Adding twice is not the same as adding once, so a client that is unsure whether a request arrived sends it again with the "
                + "same Idempotency-Key header: the product is added once and the answer of the first request is returned again, with the "
                + "header Idempotent-Replayed: true. A key belongs to its requester and is kept for 24 hours. The same key with another body "
                + "answers 422, and a repeat that arrives while the first request still runs answers 409 with Retry-After.")
            .Produces<CartDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblems(
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .AddEndpointFilter<RequireRequesterFilter>()
            .AddEndpointFilter<IdempotencyFilter>();

        carts.MapPut("/{cartId:guid}/items/{productId}", ChangeQuantity)
            .WithName("ChangeItemQuantity")
            .WithSummary("Sets the quantity of a product in the cart.")
            .Produces<CartDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblems(
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .AddEndpointFilter<RequireRequesterFilter>();

        carts.MapDelete("/{cartId:guid}/items/{productId}", RemoveItem)
            .WithName("RemoveItem")
            .WithSummary("Removes a product from the cart. Removing a product that is not there is not an error.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblems(
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .AddEndpointFilter<RequireRequesterFilter>();

        carts.MapPost("/{cartId:guid}/checkout", Checkout)
            .WithName("Checkout")
            .WithSummary("Starts checkout of the cart.")
            .WithDescription("Only a signed-in customer can check out, and the cart must not be empty. The cart becomes read-only and the "
                + "Order service is told through a CartCheckedOut event, so the answer is 202 Accepted. Repeating the request answers "
                + "202 with the same checkoutId and publishes nothing new.")
            .Produces<CheckoutAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblems(
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting(RateLimitingRegistration.StrictPolicy)
            .AddEndpointFilter<RequireRequesterFilter>();

        return endpoints;
    }

    private static async Task<IResult> CreateCart(
        HttpContext httpContext,
        RequesterResolver requesters,
        CreateCartHandler handler,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.Headers.ContainsKey(HeaderNames.Authorization) && httpContext.User.Identity?.IsAuthenticated != true)
        {
            // The client says it is somebody, and the proof is bad. Answering as if it were a visitor would give a
            // customer with an expired token a guest cart that has nothing to do with their own.
            httpContext.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer error=\"invalid_token\"";

            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "The bearer token is not valid.",
                detail: "The token is expired, was not issued for this API, or has a bad signature. Sign in again, or leave out the Authorization header to create a guest cart.");
        }

        Guid? customerId = requesters.Resolve(httpContext)?.CustomerId;

        CreateCartResult result = await handler.HandleAsync(customerId, cancellationToken);
        string location = $"/v1/carts/{result.Cart.Id}";

        if (result.GuestToken is not null)
        {
            return TypedResults.Created(location, new GuestCartCreatedResponse { GuestToken = result.GuestToken, Cart = result.Cart });
        }

        return result.Created ? TypedResults.Created(location, result.Cart) : TypedResults.Ok(result.Cart);
    }

    private static async Task<IResult> GetMyCart(
        HttpContext httpContext,
        GetMyCartHandler handler,
        CancellationToken cancellationToken)
    {
        Requester requester = RequireRequesterFilter.GetRequester(httpContext);
        CartDto cart = await handler.HandleAsync(requester.CustomerId!.Value, cancellationToken);

        return TypedResults.Ok(cart);
    }

    private static async Task<IResult> MergeGuestCart(
        HttpContext httpContext,
        RequesterResolver requesters,
        MergeGuestCartHandler handler,
        CancellationToken cancellationToken)
    {
        string? guestTokenHash = requesters.ResolveGuestTokenHash(httpContext);

        if (guestTokenHash is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [RequesterResolver.GuestTokenHeader] = new[] { "The header with the secret token of the guest cart is required." },
            });
        }

        Requester requester = RequireRequesterFilter.GetRequester(httpContext);
        CartDto cart = await handler.HandleAsync(requester.CustomerId!.Value, guestTokenHash, cancellationToken);

        return TypedResults.Ok(cart);
    }

    private static async Task<IResult> GetCart(
        Guid cartId,
        HttpContext httpContext,
        GetCartHandler handler,
        CancellationToken cancellationToken)
    {
        Requester requester = RequireRequesterFilter.GetRequester(httpContext);
        CartDto cart = await handler.HandleAsync(cartId, requester, cancellationToken);

        return TypedResults.Ok(cart);
    }

    private static async Task<IResult> AddItem(
        Guid cartId,
        AddItemRequest request,
        HttpContext httpContext,
        AddItemHandler handler,
        CancellationToken cancellationToken)
    {
        Requester requester = RequireRequesterFilter.GetRequester(httpContext);
        CartDto cart = await handler.HandleAsync(cartId, requester, request.ProductId, request.Quantity, cancellationToken);

        return TypedResults.Ok(cart);
    }

    private static async Task<IResult> ChangeQuantity(
        Guid cartId,
        [StringLength(64, MinimumLength = 1)] string productId,
        ChangeQuantityRequest request,
        HttpContext httpContext,
        ChangeItemQuantityHandler handler,
        CancellationToken cancellationToken)
    {
        Requester requester = RequireRequesterFilter.GetRequester(httpContext);
        CartDto cart = await handler.HandleAsync(cartId, requester, productId, request.Quantity, cancellationToken);

        return TypedResults.Ok(cart);
    }

    private static async Task<IResult> RemoveItem(
        Guid cartId,
        [StringLength(64, MinimumLength = 1)] string productId,
        HttpContext httpContext,
        RemoveItemHandler handler,
        CancellationToken cancellationToken)
    {
        Requester requester = RequireRequesterFilter.GetRequester(httpContext);
        await handler.HandleAsync(cartId, requester, productId, cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> Checkout(
        Guid cartId,
        HttpContext httpContext,
        CheckoutHandler handler,
        CancellationToken cancellationToken)
    {
        Requester requester = RequireRequesterFilter.GetRequester(httpContext);
        CheckoutResult result = await handler.HandleAsync(cartId, requester, cancellationToken);

        return TypedResults.Accepted((string?)null, new CheckoutAcceptedResponse { CheckoutId = result.CheckoutId });
    }

    private static RouteHandlerBuilder ProducesProblems(this RouteHandlerBuilder builder, params int[] statusCodes)
    {
        foreach (int statusCode in statusCodes)
        {
            builder.ProducesProblem(statusCode);
        }

        return builder;
    }
}
