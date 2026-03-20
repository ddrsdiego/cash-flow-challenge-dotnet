using System;
using System.Reflection;
using CashFlow.SharedKernel.Interfaces;
using CashFlow.SharedKernel.Infrastructure.Messaging;
using CashFlow.SharedKernel.Messages;
using CashFlow.Transactions.API.Infrastructure.MongoDB;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CashFlow.Transactions.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddMongoDb(this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration["MongoDB:ConnectionString"]
                               ?? throw new InvalidOperationException("MongoDB:ConnectionString is required.");

        var username = configuration["MongoDB:Username"];
        var password = configuration["MongoDB:Password"];
        var authSource = configuration["MongoDB:AuthSource"];

        var settings = MongoClientSettings.FromConnectionString(connectionString);

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            settings.Credential = MongoCredential.CreateCredential(authSource, username, password);

        services.AddScoped<MongoDbContext>();
        services.TryAddSingleton<IMongoClient>(_ => new MongoClient(settings));
        services.AddScoped<ITransactionRepository, TransactionRepository>();
    }

    public static void AddMassTransitWithRabbitMq(this IServiceCollection services,
        IConfiguration configuration)
    {
        var host = configuration["RabbitMQ:HostName"] ?? configuration["RabbitMQ:Host"];
        var virtualHost = configuration["RabbitMQ:VirtualHost"];
        var username = configuration["RabbitMQ:Username"];
        var password = configuration["RabbitMQ:Password"];
        var databaseName = configuration["MongoDB:DatabaseName"];

        services.AddMassTransit(x =>
        {
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(host, virtualHost, h =>
                {
                    h.Username(username);
                    h.Password(password);
                });
                cfg.Message<TransactionCreatedEvent>(m =>
                    m.SetEntityName("cashflow.transactions"));
                cfg.Publish<TransactionCreatedEvent>(p =>
                    p.ExchangeType = "fanout");
                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddScoped<ITransactionalPublisher, TransactionalPublisher>();
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
                    ValidateLifetime = true
                };
            });

        services.AddAuthorization();

        return services;
    }

    public static IServiceCollection AddOpenTelemetryInstrumentation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceName = configuration["OpenTelemetry:ServiceName"] ?? "cashflow-transactions-api";
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

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