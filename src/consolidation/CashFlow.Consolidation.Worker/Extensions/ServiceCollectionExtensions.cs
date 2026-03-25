using System;
using System.Reflection;
using CashFlow.Consolidation.Worker.Infrastructure.MongoDB;
using CashFlow.SharedKernel.Infrastructure.Extensions;
using CashFlow.SharedKernel.Infrastructure.Messaging;
using CashFlow.SharedKernel.Infrastructure.MongoIndex;
using CashFlow.SharedKernel.Interfaces;
using CashFlow.SharedKernel.Messages;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace CashFlow.Consolidation.Worker.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddMongoDb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMongoDbClient(configuration);
        services.AddScoped<ICashFlowMongoDbContext, CashFlowMongoDbContext>();
        services.AddScoped<IReceivedTransactionRepository, ReceivedTransactionRepository>();
        services.AddScoped<IConsolidationRepository, ConsolidationRepository>();
        
        // Register MongoDB index configurators
        services.AddScoped<IDailyBalancesIndexConfig, DailyBalancesIndexConfig>();
        services.AddScoped<IMongoIndexConfigurator>(provider => provider.GetRequiredService<IDailyBalancesIndexConfig>());
        
        services.AddScoped<IReceivedTransactionIndexConfig, ReceivedTransactionIndexConfig>();
        services.AddScoped<IMongoIndexConfigurator>(provider => provider.GetRequiredService<IReceivedTransactionIndexConfig>());
    }

    public static void AddMassTransitWithRabbitMq(this IServiceCollection services, IConfiguration configuration)
    {
        var host = configuration["RabbitMQ:HostName"] ?? configuration["RabbitMQ:Host"];
        var port = configuration.GetValue("RabbitMQ:Port", 5672);
        var virtualHost = configuration["RabbitMQ:VirtualHost"] ?? "/";
        var username = configuration["RabbitMQ:UserName"] ?? configuration["RabbitMQ:Username"];
        var password = configuration["RabbitMQ:Password"];
        var databaseName = configuration["MongoDB:DatabaseName"];

        services.AddMassTransit(x =>
        {
            x.AddConsumers(typeof(ServiceCollectionExtensions).Assembly);

            x.AddMongoDbOutbox(o =>
            {
                o.QueryDelay = TimeSpan.FromSeconds(1);
                o.ClientFactory(provider => provider.GetRequiredService<IMongoClient>());
                o.DatabaseFactory(provider => provider.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
                o.UseBusOutbox();
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(host, (ushort)port, virtualHost, h =>
                {
                    h.Username(username);
                    h.Password(password);
                });

                cfg.Message<TransactionCreatedEvent>(m =>
                    m.SetEntityName("cashflow.transactions"));

                cfg.Publish<TransactionCreatedEvent>(p =>
                    p.ExchangeType = "fanout");

                cfg.Message<DailyConsolidationUpdatedEvent>(m =>
                    m.SetEntityName("cashflow.consolidation"));

                cfg.Publish<DailyConsolidationUpdatedEvent>(p =>
                    p.ExchangeType = "fanout");

                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddScoped<ITransactionalPublisher, TransactionalPublisher>();
    }

    public static void AddMediatRHandlers(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
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