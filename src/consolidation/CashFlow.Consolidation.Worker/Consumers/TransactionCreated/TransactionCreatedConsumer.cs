using CashFlow.Consolidation.Worker.Consumers.TransactionCreated.Extensions;
using CashFlow.Consolidation.Worker.Extensions;
using CashFlow.SharedKernel.Messages;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace CashFlow.Consolidation.Worker.Consumers.TransactionCreated;

/// <summary>
/// Consumes TransactionCreatedEvent from RabbitMQ topic (cashflow.transactions, routing: transaction.created).
/// Maps the event to IngestTransactionsBatchCommand and dispatches via MediatR.
/// Uses MongoDB Outbox (via ConsumerDefinition) for transactional consistency.
/// </summary>
public sealed class TransactionCreatedConsumer :
    IConsumer<TransactionCreatedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<TransactionCreatedConsumer> _logger;

    public TransactionCreatedConsumer(
        IMediator mediator,
        ILogger<TransactionCreatedConsumer> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<TransactionCreatedEvent> context)
    {
        var tracerId = context.Message.TracerId;

        TransactionCreatedConsumerLog.ReceivedEventFromTopic(_logger, tracerId, context.Message.BatchId);

        try
        {
            var command = context.ToCommand();
            var response = await _mediator.Send(command);
            response.ThrowIfServerError(_logger, "transaction-created-consumer", tracerId);

            TransactionCreatedConsumerLog.ProcessedSuccessfully(_logger, tracerId, context.Message.BatchId);
        }
        catch (Exception ex)
        {
            TransactionCreatedConsumerLog.ProcessingError(_logger, ex, tracerId, context.Message.BatchId);
            throw;
        }
    }
}