namespace CartService.Api.Observability;

using System;
using System.Reflection;
using CartService.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

/// <summary>
/// Traces, metrics and logs with OpenTelemetry. The service always produces them; they leave the process only
/// when an OTLP endpoint is configured (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>), so a run without a collector has no
/// export errors. In Azure the endpoint is the one of the collector that feeds Azure Monitor.
/// </summary>
internal static class ObservabilityRegistration
{
    public const string ServiceName = "cartservice";

    private const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static WebApplicationBuilder AddCartObservability(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The Azure SDK, and with it Service Bus, only creates spans when this is switched on.
        AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

        OpenTelemetryBuilder telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName, serviceVersion: Version()))
            .WithTracing(tracing => tracing
                .SetSampler(new DropOrphanClientSpansSampler())
                .AddAspNetCoreInstrumentation(options => options.Filter = IsNotHealthProbe)
                .AddHttpClientInstrumentation()
                .AddSource(CartTelemetry.Name, "Npgsql", "Azure.*"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(CartTelemetry.Name, "Npgsql", "Microsoft.AspNetCore.RateLimiting"))
            .WithLogging();

        if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]))
        {
            telemetry.UseOtlpExporter();
        }

        if (!builder.Environment.IsDevelopment())
        {
            // One JSON document per line: the platform collects the standard output of the container, and a log
            // that is already structured needs no parsing there.
            builder.Logging.AddJsonConsole();
        }

        return builder;
    }

    // Probes arrive every few seconds from every instance and would drown the traces that matter.
    private static bool IsNotHealthProbe(Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        return !httpContext.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
    }

    private static string Version()
    {
        return typeof(ObservabilityRegistration).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    }
}
