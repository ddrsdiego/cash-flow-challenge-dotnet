using CashFlow.Gateway.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Collections.Generic;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog as logging provider
builder.Host.UseSerilog((context, cfg) =>
{
    var otlpEndpoint = context.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";
    var serviceName = context.Configuration["OpenTelemetry:ServiceName"] ?? "cashflow-gateway";

    cfg.ReadFrom.Configuration(context.Configuration)  // Console sink from appsettings
       .WriteTo.OpenTelemetry(opts =>                  // OTel sink (OTLP gRPC)
       {
           opts.Endpoint = otlpEndpoint;
           opts.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc;
           opts.ResourceAttributes = new Dictionary<string, object>
           {
               ["service.name"] = serviceName
           };
       });
});

// ─────────────────────────────────────────────────────────────────────────────
// Services Registration
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddJwtAuthentication(builder.Configuration);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("require-admin", policy => policy.RequireRole("admin"));
    options.AddPolicy("require-user", policy => policy.RequireRole("admin", "merchant"));
});

builder.Services.AddRateLimitingPolicies(builder.Configuration);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddOpenTelemetryInstrumentation(builder.Configuration);

// ─────────────────────────────────────────────────────────────────────────────
// Middleware Pipeline
// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// ─────────────────────────────────────────────────────────────────────────────
// Endpoints
// ─────────────────────────────────────────────────────────────────────────────

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "cashflow-gateway" }))
    .WithMetadata(new EndpointNameMetadata("Health"))
    .AllowAnonymous();

app.MapReverseProxy();

app.Run();
