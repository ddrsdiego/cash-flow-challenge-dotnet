namespace CashFlow.Consolidation.API.Consumers;

using System;
using CashFlow.SharedKernel.Infrastructure;
using MassTransit;

public sealed class DailyConsolidationUpdatedConsumerDefinition :
    ConsumerDefinition<DailyConsolidationUpdatedConsumer>
{
    public DailyConsolidationUpdatedConsumerDefinition()
    {
        // Fanout per-instance: each pod gets a unique queue that is auto-deleted when the pod stops.
        // This ensures that all pods receive cache invalidation messages simultaneously,
        // rather than competing for a single shared queue (competing consumers pattern).
        EndpointName = $"{RabbitMqEndpointNames.DailyConsolidationUpdatedCache.QueueName}-{Guid.NewGuid():N}";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<DailyConsolidationUpdatedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rmq)
        {
            // Queue is ephemeral: auto-deleted when the consumer connection closes
            rmq.AutoDelete = true;
            rmq.Durable = false;

            rmq.Bind(RabbitMqEndpointNames.DailyConsolidationUpdatedCache.Exchange,
                x =>
                {
                    // Exchange is permanent: survives pod restarts
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
    }
}
