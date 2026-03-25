using System;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Messages;
using CashFlow.Transactions.Worker.Consumers.Extensions;
using CashFlow.Transactions.Worker.Extensions;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CashFlow.Transactions.Worker.Consumers;

/// <summary>
/// Consumes TransactionBatchReadyEvent from RabbitMQ queue: transaction.batch.process.
/// Maps to ProcessTransactionBatchCommand and dispatches via MediatR.
/// Uses MongoDB Outbox (via ConsumerDefinition) for transactional consistency.
/// </summary>
public sealed class TransactionBatchReadyConsumer :
    IConsumer<TransactionBatchReadyEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<TransactionBatchReadyConsumer> _logger;

    public TransactionBatchReadyConsumer(
        IMediator mediator,
        ILogger<TransactionBatchReadyConsumer> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<TransactionBatchReadyEvent> context)
    {
        var tracerId = context.Message.TracerId;

        TransactionBatchReadyConsumerLog.ReceivedEventFromQueue(_logger, tracerId, context.Message.BatchId);

        try
        {
            var command = context.ToCommand();
            
            var response = await _mediator.Send(command);
            response.ThrowIfServerError(_logger, "transaction-batch-ready-consumer", tracerId);

            TransactionBatchReadyConsumerLog.ProcessedSuccessfully(_logger, tracerId, context.Message.BatchId);
        }
        catch (Exception ex)
        {
            TransactionBatchReadyConsumerLog.ProcessingError(_logger, ex, tracerId, context.Message.BatchId);
            throw;
        }
    }
}
