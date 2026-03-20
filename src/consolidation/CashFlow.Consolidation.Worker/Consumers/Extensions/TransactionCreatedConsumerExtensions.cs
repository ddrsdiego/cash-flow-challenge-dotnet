using CashFlow.Consolidation.Worker.Application.UseCases.IngestTransactionsBatch;
using CashFlow.SharedKernel.Messages;
using MassTransit;

namespace CashFlow.Consolidation.Worker.Consumers.Extensions;

/// <summary>
/// Extension method to map TransactionCreatedEvent to IngestTransactionsBatchCommand.
/// </summary>
public static class TransactionCreatedConsumerExtensions
{
    public static IngestTransactionsBatchCommand ToCommand(
        this ConsumeContext<TransactionCreatedEvent> context) =>
        new(
            context.Message.TracerId,
            context.Message.BatchId,
            context.Message.Transactions);
}
