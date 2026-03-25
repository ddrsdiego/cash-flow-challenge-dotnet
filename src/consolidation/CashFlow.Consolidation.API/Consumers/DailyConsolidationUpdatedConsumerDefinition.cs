namespace CashFlow.Consolidation.API.Consumers;

using System;
using CashFlow.SharedKernel.Infrastructure;
using MassTransit;

public sealed class DailyConsolidationUpdatedConsumerDefinition :
    ConsumerDefinition<DailyConsolidationUpdatedConsumer>
{
    public DailyConsolidationUpdatedConsumerDefinition()
    {
        EndpointName = RabbitMqEndpointNames.DailyConsolidationUpdatedCache.QueueName;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<DailyConsolidationUpdatedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rmq)
        {
            rmq.Bind(RabbitMqEndpointNames.DailyConsolidationUpdatedCache.Exchange,
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

        // NO MongoDB Outbox for this consumer — read-only operations only
    }
}
