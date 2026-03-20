using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Sinks.OpenTelemetry;

namespace CashFlow.SharedKernel.Infrastructure.Extensions;

/// <summary>
/// Centralizes Serilog configuration with OpenTelemetry sink.
/// Provides structured logging to Seq/Jaeger via OTLP gRPC.
/// </summary>
public static class SerilogExtensions
{
    /// <summary>
    /// Configures Serilog with console sink (from appsettings) and OpenTelemetry sink (OTLP gRPC).
    /// </summary>
    /// <param name="builder">The host builder</param>
    /// <returns>The configured host builder</returns>
    public static IHostBuilder AddSerilogWithOpenTelemetry(this IHostBuilder builder)
    {
        return builder.UseSerilog((context, cfg) =>
        {
            var otlpEndpoint = context.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";
            var serviceName = context.Configuration["OpenTelemetry:ServiceName"] 
                              ?? context.HostingEnvironment.ApplicationName;

            cfg.ReadFrom.Configuration(context.Configuration)  // Console sink from appsettings
               .WriteTo.OpenTelemetry(opts =>                  // OTel sink (OTLP gRPC)
               {
                   opts.Endpoint = otlpEndpoint;
                   opts.Protocol = OtlpProtocol.Grpc;
                   opts.ResourceAttributes = new Dictionary<string, object>
                   {
                       ["service.name"] = serviceName
                   };
               });
        });
    }
}
