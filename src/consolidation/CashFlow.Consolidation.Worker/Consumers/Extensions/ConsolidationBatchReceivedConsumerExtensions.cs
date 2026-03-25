using CashFlow.Consolidation.Worker.Application.UseCases.ProcessConsolidationBatch;
using CashFlow.SharedKernel.Messages;
using MassTransit;

namespace CashFlow.Consolidation.Worker.Consumers.Extensions;

/// <summary>
/// Extension method to map ConsolidationBatchReceivedEvent to ProcessConsolidationBatchCommand.
/// </summary>
public static class ConsolidationBatchReceivedConsumerExtensions
{
    public static ProcessConsolidationBatchCommand ToCommand(
        this ConsumeContext<ConsolidationBatchReceivedEvent> context) =>
        new(
            context.Message.TracerId,
            context.Message.BatchId);
}
