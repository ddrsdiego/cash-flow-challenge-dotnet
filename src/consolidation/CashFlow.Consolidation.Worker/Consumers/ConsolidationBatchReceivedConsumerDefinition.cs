using System;
using CashFlow.SharedKernel.Infrastructure;
using MassTransit;

namespace CashFlow.Consolidation.Worker.Consumers;

/// <summary>
/// Configures ConsolidationBatchReceivedConsumer with:
/// - RabbitMQ fanout exchange binding (cashflow.consolidation → consolidation.updated)
/// - Message retry policy for resilience
/// - MongoDB Outbox for transactional consistency
/// 
/// Fanout exchange broadcasts messages to all bound queues without routing key filtering.
/// </summary>
public sealed class ConsolidationBatchReceivedConsumerDefinition :
    ConsumerDefinition<ConsolidationBatchReceivedConsumer>
{
    public ConsolidationBatchReceivedConsumerDefinition()
    {
        EndpointName = RabbitMqEndpointNames.DailyConsolidationUpdated.QueueName;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ConsolidationBatchReceivedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rmq)
        {
            rmq.Bind(RabbitMqEndpointNames.DailyConsolidationUpdated.Exchange,
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
