namespace CartService.Api.ErrorHandling;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using CartService.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

/// <summary>
/// Turns exceptions into RFC 7807 problem details. Business errors get a specific status and a stable
/// <c>code</c>; anything unexpected is logged and answered with a generic 500 that reveals nothing.
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problemDetails = Map(exception);

        if (problemDetails.Status == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            _logger.LogInformation("{Method} {Path} was rejected: {Title}", httpContext.Request.Method, httpContext.Request.Path, problemDetails.Title);
        }

        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception,
        });
    }

    private static ProblemDetails Map(Exception exception)
    {
        switch (exception)
        {
            case CartNotFoundException:
                return Problem(StatusCodes.Status404NotFound, "The cart was not found.", exception.Message, "cart.not_found");

            case CartRuleViolationException violation:
                return Problem(StatusCodes.Status422UnprocessableEntity, "The request breaks a rule of the cart.", violation.Message, violation.Code);

            case ConcurrencyConflictException:
                return Problem(StatusCodes.Status409Conflict, "The cart was changed by another request.", exception.Message, "cart.concurrency_conflict");

            case ActiveCartAlreadyExistsException:
                return Problem(StatusCodes.Status409Conflict, "The customer already has an active cart.", exception.Message, "cart.active_cart_exists");

            default:
                return Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null, null);
        }
    }

    private static ProblemDetails Problem(int status, string title, string? detail, string? code)
    {
        ProblemDetails problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
        };

        if (code is not null)
        {
            problemDetails.Extensions["code"] = code;
        }

        return problemDetails;
    }
}
