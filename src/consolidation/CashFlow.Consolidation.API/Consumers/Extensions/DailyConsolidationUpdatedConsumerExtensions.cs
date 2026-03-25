namespace CashFlow.Consolidation.API.Consumers.Extensions;

using CashFlow.Consolidation.API.Application.UseCases.UpdateConsolidationCache;
using CashFlow.SharedKernel.Messages;
using MassTransit;

public static class DailyConsolidationUpdatedConsumerExtensions
{
    public static UpdateConsolidationCacheCommand ToCommand(
        this ConsumeContext<DailyConsolidationUpdatedEvent> context) =>
        new(
            context.Message.TracerId,
            context.Message.ConsolidationKeys);
}
