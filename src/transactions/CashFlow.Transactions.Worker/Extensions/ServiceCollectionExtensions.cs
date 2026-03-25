using System;
using System.Reflection;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Infrastructure;
using CashFlow.SharedKernel.Infrastructure.Extensions;
using CashFlow.SharedKernel.Infrastructure.Messaging;
<<<<<<< HEAD
using CashFlow.SharedKernel.Infrastructure.MongoIndex;
=======
>>>>>>> 55c15ded73d5f33778101db5027f405e73103f12
using CashFlow.SharedKernel.Interfaces;
using CashFlow.SharedKernel.Messages;
using CashFlow.Transactions.Worker.Infrastructure.MongoDB;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace CashFlow.Transactions.Worker.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddMongoDb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMongoDbClient(configuration);
        services.AddScoped<ITransactionsWorkerMongoDbContext, TransactionsWorkerMongoDbContext>();
        services.AddScoped<IRawRequestRepository, RawRequestRepository>();
        services.AddScoped<IDistributedLockRepository, DistributedLockRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
<<<<<<< HEAD
        
        // Register MongoDB index configurators
        services.AddScoped<IRawRequestIndexConfig, RawRequestIndexConfig>();
        services.AddScoped<IMongoIndexConfigurator>(provider => provider.GetRequiredService<IRawRequestIndexConfig>());
        
        services.AddScoped<IDistributedLockIndexConfig, DistributedLockIndexConfig>();
        services.AddScoped<IMongoIndexConfigurator>(provider => provider.GetRequiredService<IDistributedLockIndexConfig>());
=======
>>>>>>> 55c15ded73d5f33778101db5027f405e73103f12
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

                cfg.Message<TransactionBatchReadyEvent>(m =>
                    m.SetEntityName(RabbitMqEndpointNames.TransactionBatchReady.Exchange));

                cfg.Publish<TransactionBatchReadyEvent>(p =>
                    p.ExchangeType = "fanout");

                cfg.Message<TransactionCreatedEvent>(m =>
                    m.SetEntityName(RabbitMqEndpointNames.TransactionCreated.Exchange));

                cfg.Publish<TransactionCreatedEvent>(p =>
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

    public static void AddBatcherBackgroundService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHostedService<Workers.BatcherBackgroundService>();
    }

<<<<<<< HEAD
=======
    public static async Task EnsureMongoDbIndexesAsync(this IHost host)
    {
        using (var scope = host.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ITransactionsWorkerMongoDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
            await MongoDbIndexSetup.EnsureIndexesAsync(context, logger);
        }
    }
>>>>>>> 55c15ded73d5f33778101db5027f405e73103f12
}
