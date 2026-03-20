using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Interfaces;
using CashFlow.SharedKernel.Messages;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CashFlow.Consolidation.Worker.Application.UseCases.IngestTransactionsBatch;

public sealed class IngestTransactionsBatchCommandHandler : IRequestHandler<IngestTransactionsBatchCommand, Response>
{
    private readonly IReceivedTransactionRepository _repository;
    private readonly ITransactionalPublisher _transactionalPublisher;
    private readonly ILogger<IngestTransactionsBatchCommandHandler> _logger;

    public IngestTransactionsBatchCommandHandler(
        IReceivedTransactionRepository repository,
        ITransactionalPublisher transactionalPublisher,
        ILogger<IngestTransactionsBatchCommandHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _transactionalPublisher = transactionalPublisher ?? throw new ArgumentNullException(nameof(transactionalPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Response> Handle(IngestTransactionsBatchCommand request, CancellationToken cancellationToken)
    {
        IngestTransactionsBatchLog.ProcessingBatch(_logger, request.TracerId, request.BatchId);

        // ═══════════════════════════════════════════════════════════════════════
        // FASE 1 - VALIDAR INPUTS
        // ═══════════════════════════════════════════════════════════════════════
        if (request.Transactions.Count == 0)
            return IngestTransactionsBatchErrors.EmptyTransactions(request.TracerId);

        if (string.IsNullOrWhiteSpace(request.BatchId))
            return IngestTransactionsBatchErrors.InvalidBatchId(request.TracerId);

        try
        {
            // ═══════════════════════════════════════════════════════════════════
            // FASE 2 - RESOLVER DEPENDÊNCIAS
            // ═══════════════════════════════════════════════════════════════════

            // Map TransactionItem[] → ReceivedTransaction[]
            var receivedTransactions = MapTransactionsAsync(request);

            // ═══════════════════════════════════════════════════════════════════
            // FASE 3 - PERSISTIR (transação atômica via MassTransit Outbox)
            // ═══════════════════════════════════════════════════════════════════
            return await PersistBatchAsync(request, receivedTransactions, cancellationToken);
        }
        catch (Exception ex)
        {
            IngestTransactionsBatchLog.UnexpectedError(_logger, ex, request.TracerId, ex.Message);
            return IngestTransactionsBatchErrors.UnexpectedError(request.TracerId, ex.Message);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MÉTODOS PRIVADOS - FASE 2
    // ═══════════════════════════════════════════════════════════════════════════

    private static List<ReceivedTransaction> MapTransactionsAsync(IngestTransactionsBatchCommand request)
    {
        return request.Transactions
            .Select(item => new ReceivedTransaction
            {
                TransactionId = item.TransactionId,
                BatchId = request.BatchId,
                UserId = item.UserId,
                Type = item.Type,
                Amount = item.Amount,
                Category = item.Category,
                Date = item.Date,
                ReceivedAt = DateTime.UtcNow,
                TracerId = request.TracerId
            })
            .ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MÉTODOS PRIVADOS - FASE 3
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<Response> PersistBatchAsync(
        IngestTransactionsBatchCommand request,
        List<ReceivedTransaction> receivedTransactions,
        CancellationToken cancellationToken)
    {
        try
        {
            // Insert transactions (atomicity guaranteed by MassTransit Outbox)
            await _repository.BulkInsertAsync(receivedTransactions, cancellationToken);

            // Publish event via MassTransit Outbox
            // The event is persisted and delivered atomically with the consumer's transaction
            var consolidationBatchReceivedEvent = new ConsolidationBatchReceivedEvent(
                request.BatchId,
                request.TracerId);

            await _transactionalPublisher.PublishAsync(consolidationBatchReceivedEvent, cancellationToken);

            IngestTransactionsBatchLog.BatchIngested(_logger, request.TracerId, receivedTransactions.Count);

            return Response.Ok();
        }
        catch (Exception e)
        {
            IngestTransactionsBatchLog.FailedToInsertTransactions(_logger, e, request.TracerId, e.Message);
            return IngestTransactionsBatchErrors.DatabaseError(request.TracerId, e.Message);
        }
    }
}
