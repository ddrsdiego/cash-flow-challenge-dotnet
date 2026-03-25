using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Interfaces;
using CashFlow.SharedKernel.Messages;
using CSharpFunctionalExtensions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CashFlow.Transactions.Worker.Application.UseCases.ProcessTransactionBatch;

/// <summary>
/// Handler for ProcessTransactionBatchCommand.
/// Atomic operation via MongoDB Outbox: bulk insert transactions + mark requests as processed + publish TransactionCreatedEvent.
/// Consumed by TransactionBatchReadyConsumer with UseMongoDbOutbox.
/// </summary>
public sealed class ProcessTransactionBatchCommandHandler :
    IRequestHandler<ProcessTransactionBatchCommand, Response>
{
    private readonly IRawRequestRepository _rawRequestRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly ITransactionalPublisher _transactionalPublisher;
    private readonly ILogger<ProcessTransactionBatchCommandHandler> _logger;

    public ProcessTransactionBatchCommandHandler(
        IRawRequestRepository rawRequestRepository,
        ITransactionRepository transactionRepository,
        ITransactionalPublisher transactionalPublisher,
        ILogger<ProcessTransactionBatchCommandHandler> logger)
    {
        _rawRequestRepository = rawRequestRepository ?? throw new ArgumentNullException(nameof(rawRequestRepository));
        _transactionRepository =
            transactionRepository ?? throw new ArgumentNullException(nameof(transactionRepository));
        _transactionalPublisher =
            transactionalPublisher ?? throw new ArgumentNullException(nameof(transactionalPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Response> Handle(ProcessTransactionBatchCommand request, CancellationToken cancellationToken)
    {
        ProcessTransactionBatchLog.ProcessingBatch(_logger, request.TracerId, request.BatchId,
            request.RawRequestIds.Count);

        // ═══════════════════════════════════════════════════════════════
        // FASE 1 - VALIDAR INPUTS
        // ═══════════════════════════════════════════════════════════════
        if (request.RawRequestIds.Count == 0)
            return ProcessTransactionBatchErrors.EmptyBatch(request.TracerId);

        if (string.IsNullOrWhiteSpace(request.BatchId))
            return ProcessTransactionBatchErrors.InvalidBatchId(request.TracerId);

        try
        {
            // ═══════════════════════════════════════════════════════════
            // FASE 2 - RESOLVER DEPENDÊNCIAS
            // Buscar raw requests do batch
            // ═══════════════════════════════════════════════════════════
            var rawRequestsResult = await GetRawRequestsByBatchAsync(request, cancellationToken);
            if (rawRequestsResult.IsFailure)
                return rawRequestsResult.Error;

            // ═══════════════════════════════════════════════════════════
            // FASE 3 - PERSISTIR (transação atômica com Outbox)
            // BulkInsert transactions + MarkAsProcessed + Publish event
            // ═══════════════════════════════════════════════════════════
            return await PersistBatchAsync(request, rawRequestsResult.Value, cancellationToken);
        }
        catch (Exception ex)
        {
            ProcessTransactionBatchLog.UnexpectedError(_logger, ex, request.TracerId, ex.Message);
            return ProcessTransactionBatchErrors.UnexpectedError(request.TracerId, ex.Message);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MÉTODOS PRIVADOS - FASE 2
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<Result<List<RawRequest>, Response>> GetRawRequestsByBatchAsync(
        ProcessTransactionBatchCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var rawRequests = await _rawRequestRepository.GetByBatchIdAsync(request.BatchId, cancellationToken);

            if (rawRequests.Count == 0)
            {
                ProcessTransactionBatchLog.NoRawRequestsFound(_logger, request.TracerId, request.BatchId);
                return Result.Failure<List<RawRequest>, Response>(
                    ProcessTransactionBatchErrors.RawRequestsNotFound(request.TracerId));
            }

            return Result.Success<List<RawRequest>, Response>(rawRequests.ToList());
        }
        catch (Exception ex)
        {
            ProcessTransactionBatchLog.FailedToGetRawRequests(_logger, ex, request.TracerId, ex.Message);
            return Result.Failure<List<RawRequest>, Response>(
                ProcessTransactionBatchErrors.DatabaseError(request.TracerId, ex.Message));
        }
    }

    private async Task<Response> PersistBatchAsync(
        ProcessTransactionBatchCommand request,
        List<RawRequest> rawRequests,
        CancellationToken cancellationToken)
    {
        try
        {
            // Map RawTransactionItem → Transaction
            var transactions = MapTransactionsFromRawRequests(request, rawRequests);

            await _transactionRepository.InsertAsync(transactions, _transactionalPublisher.Session, cancellationToken);

            await _rawRequestRepository.MarkAsProcessedAsync(request.RawRequestIds, _transactionalPublisher.Session,
                cancellationToken);

            var transactionItems = transactions.Select(t => new TransactionItem(
                TransactionId: t.Id,
                UserId: t.UserId,
                Type: t.Type,
                Amount: t.Amount,
                Category: t.Category,
                Date: t.Date
            )).ToList();

            var createdEvent = new TransactionCreatedEvent(
                request.BatchId,
                request.TracerId,
                transactionItems.AsReadOnly());

            await _transactionalPublisher.PublishAsync(createdEvent, cancellationToken);

            ProcessTransactionBatchLog.BatchProcessed(_logger, request.TracerId, request.BatchId, transactions.Count);

            return Response.Ok();
        }
        catch (Exception ex)
        {
            ProcessTransactionBatchLog.FailedToPersistBatch(_logger, ex, request.TracerId, request.BatchId, ex.Message);
            return ProcessTransactionBatchErrors.PersistenceError(request.TracerId, ex.Message);
        }
    }

    private static List<Transaction> MapTransactionsFromRawRequests(
        ProcessTransactionBatchCommand request,
        List<RawRequest> rawRequests)
    {
        var transactions = new List<Transaction>();

        foreach (var rawRequest in rawRequests)
        {
            foreach (var rawItem in rawRequest.Transactions)
            {
                var transaction = new Transaction
                {
                    UserId = rawItem.UserId,
                    Type = rawItem.Type,
                    Amount = rawItem.Amount,
                    Description = rawItem.Description,
                    Category = rawItem.Category,
                    Date = rawItem.Date,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                transactions.Add(transaction);
            }
        }

        return transactions;
    }
}