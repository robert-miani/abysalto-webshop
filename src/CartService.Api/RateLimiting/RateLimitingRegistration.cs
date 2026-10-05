namespace CartService.Api.RateLimiting;

using System;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using CartService.Api.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

internal static class RateLimitingRegistration
{
    public const string DefaultPolicy = "cart";
    public const string StrictPolicy = "cart-strict";
    public const string ExceededCode = "rate_limit.exceeded";

    public static IServiceCollection AddCartRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;
            options.AddPolicy(DefaultPolicy, httpContext => Partition(httpContext, strict: false));
            options.AddPolicy(StrictPolicy, httpContext => Partition(httpContext, strict: true));
        });

        return services;
    }

    // Every requester has a bucket of their own: a signed-in customer by customer id, everybody else by IP
    // address. A guest cart token is not a safe key, because nothing proves it is valid at this point, so a
    // client could pick a new one for every request and never run out of tokens.
    private static RateLimitPartition<string> Partition(HttpContext httpContext, bool strict)
    {
        RateLimitingOptions options = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

        if (!options.Enabled)
        {
            return RateLimitPartition.GetNoLimiter("disabled");
        }

        RateLimitBucket bucket = strict ? options.Strict : options.Default;
        string requester = RequesterKey(httpContext);

        // The same requester has separate buckets for the two policies, because the key includes the policy.
        return RateLimitPartition.GetTokenBucketLimiter($"{(strict ? "strict" : "default")}|{requester}", _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = bucket.TokenLimit,
            TokensPerPeriod = bucket.TokensPerPeriod,
            ReplenishmentPeriod = bucket.ReplenishmentPeriod,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    private static string RequesterKey(HttpContext httpContext)
    {
        string claim = httpContext.RequestServices.GetRequiredService<IOptions<CartAuthenticationOptions>>().Value.CustomerIdClaim;

        if (httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.FindFirstValue(claim) is { Length: > 0 } customerId)
        {
            return $"customer:{customerId}";
        }

        IPAddress? address = httpContext.Connection.RemoteIpAddress;

        return $"ip:{address?.ToString() ?? "unknown"}";
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        HttpContext httpContext = context.HttpContext;
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            httpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        ProblemDetails problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests.",
            Detail = "Slow down and send the request again after the number of seconds in the Retry-After header.",
        };
        problem.Extensions["code"] = ExceededCode;

        IProblemDetailsService problemDetails = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
    }
}
