namespace CashFlow.Consolidation.API.Consumers;

using System;
using System.Threading.Tasks;
using CashFlow.Consolidation.API.Consumers.Extensions;
using CashFlow.Consolidation.API.Extensions;
using CashFlow.Consolidation.API.Infrastructure.Utils;
using CashFlow.SharedKernel.Messages;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class DailyConsolidationUpdatedConsumer :
    IConsumer<DailyConsolidationUpdatedEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<DailyConsolidationUpdatedConsumer> _logger;

    public DailyConsolidationUpdatedConsumer(
        IMediator mediator,
        ILogger<DailyConsolidationUpdatedConsumer> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<DailyConsolidationUpdatedEvent> context)
    {
        var tracerId = context.Message.TracerId;

        DailyConsolidationUpdatedConsumerLog.ReceivedEventFromTopic(_logger, tracerId, context.Message.BatchId);

        try
        {
            var command = context.ToCommand();
            var response = await _mediator.Send(command);
            response.ThrowIfServerError(_logger, LogTypes.DailyConsolidationUpdatedConsumer, tracerId);

            DailyConsolidationUpdatedConsumerLog.CacheUpdatedSuccessfully(_logger, tracerId, context.Message.BatchId);
        }
        catch (Exception ex)
        {
            DailyConsolidationUpdatedConsumerLog.CacheUpdateError(_logger, ex, tracerId, context.Message.BatchId);
            throw;
        }
    }
}
