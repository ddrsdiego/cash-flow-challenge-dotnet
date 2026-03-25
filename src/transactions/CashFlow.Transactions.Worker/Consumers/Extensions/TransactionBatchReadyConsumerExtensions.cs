using CashFlow.SharedKernel.Messages;
using CashFlow.Transactions.Worker.Application.UseCases.ProcessTransactionBatch;
using MassTransit;

namespace CashFlow.Transactions.Worker.Consumers.Extensions;

public static class TransactionBatchReadyConsumerExtensions
{
    public static ProcessTransactionBatchCommand ToCommand(this ConsumeContext<TransactionBatchReadyEvent> context) =>
        new(
            BatchId: context.Message.BatchId,
            TracerId: context.Message.TracerId,
            RawRequestIds: context.Message.RawRequestIds);
}
