using CashFlow.SharedKernel.Infrastructure.Extensions;
using CashFlow.Transactions.API.Endpoints.Transactions;
using CashFlow.Transactions.API.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using System;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CashFlow Transactions API",
        Version = "v1",
        Description = "API for managing financial transactions (debits and credits)"
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
            Array.Empty<string>()
        }
    });
});

builder.Host.AddSerilogWithOpenTelemetry();
builder.Services.AddMongoDb(builder.Configuration);
builder.Services.AddMediatRHandlers();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddOpenTelemetryInstrumentation(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "CashFlow Transactions API v1"); });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapTransactionEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "cashflow-transactions-api" }))
    .WithMetadata(new EndpointNameMetadata("Health"))
    .AllowAnonymous();

app.Run();