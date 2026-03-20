using CashFlow.Gateway.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────────────────────
// Services Registration
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddJwtAuthentication(builder.Configuration);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());
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
