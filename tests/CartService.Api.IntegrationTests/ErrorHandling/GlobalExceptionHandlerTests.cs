namespace CartService.Api.IntegrationTests.ErrorHandling;

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CartService.Api.ErrorHandling;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using CartService.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task ACartThatIsNotFoundBecomesA404()
    {
        JsonElement problem = await HandleAsync(new CartNotFoundException());

        problem.GetProperty("status").GetInt32().ShouldBe(404);
        problem.GetProperty("code").GetString().ShouldBe("cart.not_found");
    }

    [Fact]
    public async Task ABrokenBusinessRuleBecomesA422WithTheRuleCode()
    {
        JsonElement problem = await HandleAsync(new CartRuleViolationException(CartErrorCodes.ItemLimitExceeded, "Too many products."));

        problem.GetProperty("status").GetInt32().ShouldBe(422);
        problem.GetProperty("code").GetString().ShouldBe("cart.item_limit_exceeded");
        problem.GetProperty("detail").GetString().ShouldBe("Too many products.");
    }

    [Fact]
    public async Task AConcurrentChangeBecomesA409()
    {
        JsonElement problem = await HandleAsync(new ConcurrencyConflictException(new InvalidOperationException("stale")));

        problem.GetProperty("status").GetInt32().ShouldBe(409);
        problem.GetProperty("code").GetString().ShouldBe("cart.concurrency_conflict");
    }

    [Fact]
    public async Task ASecondActiveCartBecomesA409()
    {
        JsonElement problem = await HandleAsync(new ActiveCartAlreadyExistsException(new InvalidOperationException("duplicate")));

        problem.GetProperty("status").GetInt32().ShouldBe(409);
        problem.GetProperty("code").GetString().ShouldBe("cart.active_cart_exists");
    }

    [Fact]
    public async Task AnUnexpectedErrorBecomesAGeneric500ThatRevealsNothing()
    {
        JsonElement problem = await HandleAsync(new InvalidOperationException("password=secret in connection string"));

        problem.GetProperty("status").GetInt32().ShouldBe(500);
        problem.GetProperty("title").GetString().ShouldBe("An unexpected error occurred.");
        problem.TryGetProperty("detail", out _).ShouldBeFalse();
        problem.TryGetProperty("code", out _).ShouldBeFalse();
        problem.ToString().ShouldNotContain("secret");
    }

    private static async Task<JsonElement> HandleAsync(Exception exception)
    {
        ServiceProvider services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        DefaultHttpContext httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Response.Body = new MemoryStream();
        GlobalExceptionHandler handler = new GlobalExceptionHandler(
            services.GetRequiredService<IProblemDetailsService>(),
            NullLogger<GlobalExceptionHandler>.Instance);

        bool handled = await handler.TryHandleAsync(httpContext, exception, TestContext.Current.CancellationToken);

        handled.ShouldBeTrue();
        httpContext.Response.Body.Position = 0;
        using JsonDocument document = await JsonDocument.ParseAsync(httpContext.Response.Body, cancellationToken: TestContext.Current.CancellationToken);

        // The HTTP status of the response and the status in the body must agree.
        httpContext.Response.StatusCode.ShouldBe(document.RootElement.GetProperty("status").GetInt32());

        return document.RootElement.Clone();
    }
}
