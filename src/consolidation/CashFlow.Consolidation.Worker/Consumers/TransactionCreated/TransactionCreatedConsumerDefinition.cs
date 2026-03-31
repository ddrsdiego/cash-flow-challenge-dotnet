using CashFlow.SharedKernel.Infrastructure;
using MassTransit;
using System;

namespace CashFlow.Consolidation.Worker.Consumers.TransactionCreated;

/// <summary>
/// Configures TransactionCreatedConsumer with:
/// - RabbitMQ fanout exchange binding (cashflow.transactions → consolidation.process)
/// - Message retry policy for resilience
/// - MongoDB Outbox for transactional consistency
/// 
/// Fanout exchange broadcasts messages to all bound queues without routing key filtering.
/// </summary>
public sealed class TransactionCreatedConsumerDefinition :
    ConsumerDefinition<TransactionCreatedConsumer>
{
    public TransactionCreatedConsumerDefinition()
    {
        EndpointName = RabbitMqEndpointNames.TransactionCreated.QueueName;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TransactionCreatedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rmq)
        {
            rmq.Bind(RabbitMqEndpointNames.TransactionCreated.Exchange,
                x =>
                {
                    x.ExchangeType = "fanout";
                    x.Durable = true;
                    x.AutoDelete = false;
                });
        }

        endpointConfigurator.UseMessageRetry(r =>
            r.Intervals(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30)));

        endpointConfigurator.UseMongoDbOutbox(context);
    }
}