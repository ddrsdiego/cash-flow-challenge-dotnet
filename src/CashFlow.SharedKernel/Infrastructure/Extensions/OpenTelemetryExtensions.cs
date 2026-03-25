#nullable enable

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CashFlow.SharedKernel.Infrastructure.Extensions;

/// <summary>
/// Centralizes OpenTelemetry configuration for distributed tracing.
/// Supports extensibility via callback for service-specific instrumentation.
/// </summary>
public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Registers OpenTelemetry tracing with OTLP exporter (gRPC to Jaeger/OTel Collector).
    /// Allows services to add their own instrumentation via callback.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration object</param>
    /// <param name="additionalTracing">Optional callback to add service-specific instrumentation (e.g., ASP.NET Core, HTTP client)</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddOpenTelemetryCore(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<TracerProviderBuilder>? additionalTracing = null)
    {
        var serviceName = configuration["OpenTelemetry:ServiceName"] ?? "cashflow-service";
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddOtlpExporter(opts =>
                {
                    opts.Endpoint = new Uri(otlpEndpoint);
                    opts.Protocol = OtlpExportProtocol.Grpc;
                });

                // Allow services to extend with their own instrumentation
                additionalTracing?.Invoke(tracing);
            });

        return services;
    }
}
