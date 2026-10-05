namespace CartService.Api.Idempotency;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.Requesters;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Makes a request safe to repeat. A client that sends an <c>Idempotency-Key</c> and does not get the answer,
/// for example because the connection broke, sends the same request with the same key again and gets the stored
/// answer instead of a second change. Without the header the request is carried out as usual.
/// </summary>
/// <remarks>
/// Only successful answers are stored. A request that failed changed nothing, so its key is given back and the
/// client can try again. The filter must run after <see cref="RequireRequesterFilter"/>: keys belong to the
/// requester, so two requesters can use the same key without seeing each other's answers.
/// </remarks>
internal sealed class IdempotencyFilter : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotent-Replayed";
    public const string RequestInProgressCode = "idempotency.request_in_progress";
    public const string KeyReusedCode = "idempotency.key_reused";

    private const int MaxKeyLength = 128;

    private readonly IIdempotencyStore _store;
    private readonly IOptions<JsonOptions> _jsonOptions;
    private readonly ILogger<IdempotencyFilter> _logger;

    public IdempotencyFilter(IIdempotencyStore store, IOptions<JsonOptions> jsonOptions, ILogger<IdempotencyFilter> logger)
    {
        _store = store;
        _jsonOptions = jsonOptions;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext httpContext = context.HttpContext;
        string key = httpContext.Request.Headers[HeaderName].ToString();

        if (key.Length == 0)
        {
            return await next(context);
        }

        if (!IsValidKey(key))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [HeaderName] = new[] { $"The key must have 1 to {MaxKeyLength} characters: letters, digits, '-', '_', '.' and ':'." },
            });
        }

        string scope = ScopeOf(RequireRequesterFilter.GetRequester(httpContext));
        string fingerprint = Fingerprint(httpContext, context.Arguments.OfType<IIdempotentRequest>().FirstOrDefault());
        IdempotencyClaim claim = await _store.BeginAsync(scope, key, fingerprint, httpContext.RequestAborted);

        switch (claim.Outcome)
        {
            case IdempotencyOutcome.Started:
                return await RunAsync(context, next, scope, key);

            case IdempotencyOutcome.Completed:
                return Replay(httpContext, claim);

            case IdempotencyOutcome.InProgress:
                httpContext.Response.Headers.RetryAfter = "1";

                return Problem(
                    StatusCodes.Status409Conflict,
                    "The request with this key is still being processed.",
                    "Wait a moment and send the same request again.",
                    RequestInProgressCode);

            default:
                return Problem(
                    StatusCodes.Status422UnprocessableEntity,
                    "The idempotency key was already used for a different request.",
                    "Use a new key for a new request, or send the original request again.",
                    KeyReusedCode);
        }
    }

    private static bool IsValidKey(string key)
    {
        return key.Length <= MaxKeyLength && key.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');
    }

    private static string ScopeOf(Requester requester)
    {
        return requester.CustomerId.HasValue
            ? $"customer:{requester.CustomerId.Value}"
            : $"guest:{requester.GuestTokenHash}";
    }

    private static IResult Problem(int status, string title, string detail, string code)
    {
        return Results.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }

    private async ValueTask<object?> RunAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next, string scope, string key)
    {
        object? result;

        try
        {
            result = await next(context);
        }
        catch
        {
            await TryReleaseAsync(scope, key);

            throw;
        }

        if (result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 } status && result is IValueHttpResult value)
        {
            await TryCompleteAsync(scope, key, status.StatusCode.Value, Serialize(value.Value));
        }
        else
        {
            await TryReleaseAsync(scope, key);
        }

        return result;
    }

    // The request is over once next has returned, so the bookkeeping must not be cancelled with it. A failure
    // here must not turn a request that worked into an error: the worst case is that the key frees itself when
    // the lease of the running request expires.
    private async Task TryCompleteAsync(string scope, string key, int statusCode, string? body)
    {
        try
        {
            await _store.CompleteAsync(scope, key, statusCode, body, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The response for the idempotency key could not be stored");
        }
    }

    private async Task TryReleaseAsync(string scope, string key)
    {
        try
        {
            await _store.ReleaseAsync(scope, key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The idempotency key could not be given back");
        }
    }

    private string? Serialize(object? value)
    {
        return value is null ? null : JsonSerializer.Serialize(value, value.GetType(), _jsonOptions.Value.SerializerOptions);
    }

    private string Fingerprint(HttpContext httpContext, IIdempotentRequest? request)
    {
        string body = Serialize(request) ?? string.Empty;
        string material = $"{httpContext.Request.Method}\n{httpContext.Request.Path.Value?.ToLowerInvariant()}\n{body}";

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static IResult Replay(HttpContext httpContext, IdempotencyClaim claim)
    {
        httpContext.Response.Headers[ReplayedHeaderName] = "true";
        int statusCode = claim.ResponseStatusCode!.Value;

        return claim.ResponseBody is null
            ? Results.StatusCode(statusCode)
            : Results.Text(claim.ResponseBody, "application/json", Encoding.UTF8, statusCode);
    }
}
