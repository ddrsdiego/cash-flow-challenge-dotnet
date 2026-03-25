using System;
using System.Security.Claims;
using System.Threading.Tasks;
using CashFlow.Consolidation.API.Application.UseCases.GetDailyConsolidation;
using CashFlow.Consolidation.API.Extensions;
using CashFlow.SharedKernel.Infrastructure.Extensions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddSerilogWithOpenTelemetry();

// ─────────────────────────────────────────────────────────────────────────────
// Services Registration
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CashFlow Consolidation API",
        Version = "v1",
        Description = "API for querying daily cash flow consolidation reports"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter JWT Bearer token"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
});

builder.Services.AddMongoDb(builder.Configuration);
builder.Services.AddMemoryCacheServices();
builder.Services.AddMassTransitWithRabbitMq(builder.Configuration);
builder.Services.AddMediatRHandlers();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddOpenTelemetryInstrumentation(builder.Configuration);

// ─────────────────────────────────────────────────────────────────────────────
// Middleware Pipeline
// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "CashFlow Consolidation API v1"); });
}

app.UseAuthentication();
app.UseAuthorization();

// ─────────────────────────────────────────────────────────────────────────────
// Endpoints
// ─────────────────────────────────────────────────────────────────────────────

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "cashflow-consolidation-api" }))
    .WithMetadata(new EndpointNameMetadata("Health"))
    .AllowAnonymous();

app.MapGet("/consolidation/{date:datetime}", HandleGetDailyConsolidation)
    .WithName("GetDailyConsolidation")
    .WithOpenApi()
    .RequireAuthorization()
    .WithDescription("Get daily consolidation for a specific date")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status500InternalServerError);

app.Run();

// ─────────────────────────────────────────────────────────────────────────────
// Endpoint Handlers
// ─────────────────────────────────────────────────────────────────────────────

async Task<IResult> HandleGetDailyConsolidation(
    DateTime date,
    IMediator mediator,
    HttpContext httpContext)
{
    var userId = httpContext.User.FindFirst("sub")?.Value 
                 ?? httpContext.User.FindFirst("user_id")?.Value
                 ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    if (string.IsNullOrWhiteSpace(userId))
        return Results.BadRequest(new { error = "User ID not found in token claims" });

    var query = new GetDailyConsolidationQuery(
        Guid.NewGuid().ToString(),
        userId,
        date.Date);

    var response = await mediator.Send(query);

    return response.StatusCode switch
    {
        200 => Results.Ok(response.Data),
        400 => Results.BadRequest(response.ErrorContent?.ErrorResponse),
        404 => Results.NotFound(response.ErrorContent?.ErrorResponse),
        _ => Results.StatusCode(response.StatusCode)
    };
}
