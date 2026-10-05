namespace CartService.Api.Health;

using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CartService.Infrastructure.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

internal static class HealthEndpoints
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness runs no checks: the process answers, so it lives. A restart must never be caused by a
        // dependency that is down, because restarting the service does not fix it.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
        });

        // Readiness decides whether the service gets traffic. Healthy and degraded answer 200, unhealthy 503.
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Ready),
            ResponseWriter = WriteReportAsync,
        });

        return endpoints;
    }

    // The report names every check and its status. It never contains exceptions, because the answer is public.
    private static Task WriteReportAsync(HttpContext httpContext, HealthReport report)
    {
        httpContext.Response.ContentType = "application/json";

        return JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                }),
            },
            Json,
            httpContext.RequestAborted);
    }
}
