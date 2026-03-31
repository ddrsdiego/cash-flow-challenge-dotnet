namespace CashFlow.Consolidation.API.Consumers.Extensions;

using CashFlow.Consolidation.API.Application.UseCases.InvalidateConsolidationCache;
using CashFlow.Consolidation.API.Application.UseCases.UpdateConsolidationCache;
using CashFlow.SharedKernel.Messages;
using MassTransit;

public static class DailyConsolidationUpdatedConsumerExtensions
{
    public static InvalidateConsolidationCacheCommand ToInvalidateCommand(
        this ConsumeContext<DailyConsolidationUpdatedEvent> context) =>
        new(
            context.Message.TracerId,
            context.Message.ConsolidationKeys);

    public static UpdateConsolidationCacheCommand ToUpdateCommand(
        this ConsumeContext<DailyConsolidationUpdatedEvent> context) =>
        new(
            context.Message.TracerId,
            context.Message.ConsolidationKeys);

    // Backwards compatibility with old method name (deprecated)
    public static UpdateConsolidationCacheCommand ToCommand(
        this ConsumeContext<DailyConsolidationUpdatedEvent> context) =>
        context.ToUpdateCommand();
}
