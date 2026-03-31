using System;
using System.Reflection;
using CashFlow.Consolidation.API.Infrastructure.Cache;
using CashFlow.Consolidation.API.Infrastructure.MongoDB;
using CashFlow.SharedKernel.Infrastructure.Extensions;
using CashFlow.SharedKernel.Interfaces;
using CashFlow.SharedKernel.Messages;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CashFlow.Consolidation.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMongoDb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMongoDbClient(configuration);
        services.AddScoped<ConsolidationApiDbContext>();
        services.AddScoped<IConsolidationQueryRepository, ConsolidationQueryRepository>();
        return services;
    }

    public static IServiceCollection AddMemoryCacheServices(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<IConsolidationCache, MemoryConsolidationCache>();
        return services;
    }

    public static IServiceCollection AddMassTransitWithRabbitMq(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var host = configuration["RabbitMQ:Host"] ?? configuration["RabbitMQ:HostName"];
        var virtualHost = configuration["RabbitMQ:VirtualHost"] ?? "/";
        var username = configuration["RabbitMQ:Username"];
        var password = configuration["RabbitMQ:Password"];

        services.AddMassTransit(x =>
        {
            x.AddConsumers(typeof(ServiceCollectionExtensions).Assembly);

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(host, virtualHost, h =>
                {
                    h.Username(username);
                    h.Password(password);
                });

                cfg.Message<DailyConsolidationUpdatedEvent>(m =>
                    m.SetEntityName("cashflow.consolidation"));

                cfg.Publish<DailyConsolidationUpdatedEvent>(p =>
                    p.ExchangeType = "fanout");

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }

    public static IServiceCollection AddMediatRHandlers(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
        return services;
    }

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
                    ValidateLifetime = true,
                    RoleClaimType = "realm_roles"
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("require-admin", policy => policy.RequireRole("admin"));
            options.AddPolicy("require-user", policy => policy.RequireRole("admin", "merchant"));
        });

        return services;
    }

    public static IServiceCollection AddOpenTelemetryInstrumentation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOpenTelemetryCore(configuration, tracing =>
        {
            tracing.AddSource("MassTransit");
        });
        return services;
    }
}
