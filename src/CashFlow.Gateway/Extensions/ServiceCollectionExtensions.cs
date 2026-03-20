using System;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CashFlow.Gateway.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authority = configuration["Authentication:Authority"]
                        ?? throw new InvalidOperationException("Authentication:Authority is required.");

        var audience = configuration["Authentication:Audience"] ?? "cashflow-api";
        var requireHttpsMetadata = configuration.GetValue("Authentication:RequireHttpsMetadata", false);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = requireHttpsMetadata;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = true,
                    ValidateIssuer = true,
                    ValidateLifetime = true
                };
            });

        return services;
    }

    public static IServiceCollection AddRateLimitingPolicies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("RateLimiting:PermitLimit", 100);
        var windowSeconds = configuration.GetValue("RateLimiting:WindowSeconds", 60);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;

            options.AddFixedWindowLimiter("fixed", limiterOptions =>
            {
                limiterOptions.PermitLimit = permitLimit;
                limiterOptions.Window = TimeSpan.FromSeconds(windowSeconds);
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = 0;
            });

            options.GlobalLimiter = PartitionedRateLimiter.Create<Microsoft.AspNetCore.Http.HttpContext, string>(
                httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    public static IServiceCollection AddOpenTelemetryInstrumentation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceName = configuration["OpenTelemetry:ServiceName"] ?? "cashflow-gateway";
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(opts =>
                    {
                        opts.Endpoint = new Uri(otlpEndpoint);
                        opts.Protocol = OtlpExportProtocol.Grpc;
                    });
            });

        return services;
    }
}
