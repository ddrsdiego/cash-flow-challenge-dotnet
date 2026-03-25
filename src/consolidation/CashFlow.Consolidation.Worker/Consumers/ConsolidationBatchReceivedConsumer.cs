using System;
using System.Threading.Tasks;
using CashFlow.Consolidation.Worker.Consumers.Extensions;
using CashFlow.Consolidation.Worker.Extensions;
using CashFlow.SharedKernel.Messages;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CashFlow.Consolidation.Worker.Consumers;

/// <summary>
/// Consumes ConsolidationBatchReceivedEvent from RabbitMQ topic (cashflow.consolidation, routing: daily-consolidation-updated).
/// Maps the event to ProcessConsolidationBatchCommand and dispatches via MediatR.
/// Uses MongoDB Outbox (via ConsumerDefinition) for transactional consistency.
/// </summary>
public sealed class ConsolidationBatchReceivedConsumer : IConsumer<ConsolidationBatchReceivedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<ConsolidationBatchReceivedConsumer> _logger;

    public ConsolidationBatchReceivedConsumer(
        IMediator mediator,
        ILogger<ConsolidationBatchReceivedConsumer> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<ConsolidationBatchReceivedEvent> context)
    {
        var tracerId = context.Message.TracerId;

        ConsolidationBatchReceivedConsumerLog.ReceivedEventFromTopic(_logger, tracerId, context.Message.BatchId);

        try
        {
            var command = context.ToCommand();
            var response = await _mediator.Send(command);
            response.ThrowIfServerError(_logger, "consolidation-batch-received-consumer", tracerId);

            ConsolidationBatchReceivedConsumerLog.ProcessedSuccessfully(_logger, tracerId, context.Message.BatchId);
        }
        catch (Exception ex)
        {
            ConsolidationBatchReceivedConsumerLog.ProcessingError(_logger, ex, tracerId, context.Message.BatchId);
            throw;
        }
    }
}
