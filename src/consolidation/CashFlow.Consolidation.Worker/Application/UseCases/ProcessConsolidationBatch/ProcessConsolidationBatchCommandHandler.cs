using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Domain.Enums;
using CashFlow.SharedKernel.Domain.ValueObjects;
using CashFlow.SharedKernel.Interfaces;
using CashFlow.SharedKernel.Messages;
using CSharpFunctionalExtensions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CashFlow.Consolidation.Worker.Application.UseCases.ProcessConsolidationBatch;

public sealed class ProcessConsolidationBatchCommandHandler :
    IRequestHandler<ProcessConsolidationBatchCommand, Response>
{
    private readonly IReceivedTransactionRepository _receivedTransactionRepository;
    private readonly IConsolidationRepository _consolidationRepository;
    private readonly ITransactionalPublisher _transactionalPublisher;
    private readonly ILogger<ProcessConsolidationBatchCommandHandler> _logger;

    public ProcessConsolidationBatchCommandHandler(
        IReceivedTransactionRepository receivedTransactionRepository,
        IConsolidationRepository consolidationRepository,
        ITransactionalPublisher transactionalPublisher,
        ILogger<ProcessConsolidationBatchCommandHandler> logger)
    {
        _receivedTransactionRepository = receivedTransactionRepository ?? throw new ArgumentNullException(nameof(receivedTransactionRepository));
        _consolidationRepository = consolidationRepository ?? throw new ArgumentNullException(nameof(consolidationRepository));
        _transactionalPublisher = transactionalPublisher ?? throw new ArgumentNullException(nameof(transactionalPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Response> Handle(ProcessConsolidationBatchCommand request, CancellationToken cancellationToken)
    {
        ProcessConsolidationBatchLog.ProcessingBatch(_logger, request.TracerId, request.BatchId);

        if (string.IsNullOrWhiteSpace(request.BatchId))
            return ProcessConsolidationBatchErrors.InvalidBatchId(request.TracerId);

        try
        {
            var receivedTransactionsResult = await GetReceivedTransactionsByBatchAsync(request, cancellationToken);
            if (receivedTransactionsResult.IsFailure)
                return receivedTransactionsResult.Error;

            var receivedTransactions = receivedTransactionsResult.Value;
            if (receivedTransactions.Count == 0)
            {
                ProcessConsolidationBatchLog.NoBatchTransactions(_logger, request.TracerId, request.BatchId);
                return Response.Ok();
            }

            var groupedByConsolidationKey = GroupTransactionsByConsolidationKey(receivedTransactions);
            
            var consolidationsResult = await GetExistingConsolidationsAsync(groupedByConsolidationKey, request, cancellationToken);
            if (consolidationsResult.IsFailure)
                return consolidationsResult.Error;

            var existingConsolidations = consolidationsResult.Value;
            
            var updatedConsolidations = ComputeUpdatedConsolidations(groupedByConsolidationKey, existingConsolidations);
            var processedDates = updatedConsolidations.Select(c => c.Date).Distinct().ToList();
            
            return await PersistConsolidationsAsync(request, updatedConsolidations, processedDates, cancellationToken);
        }
        catch (Exception ex)
        {
            ProcessConsolidationBatchLog.UnexpectedError(_logger, ex, request.TracerId, ex.Message);
            return ProcessConsolidationBatchErrors.UnexpectedError(request.TracerId, ex.Message);
        }
    }
    
    private async Task<Result<IReadOnlyCollection<ReceivedTransaction>, Response>> GetReceivedTransactionsByBatchAsync(
        ProcessConsolidationBatchCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var transactions = await _receivedTransactionRepository.GetByBatchIdAsync(request.BatchId, cancellationToken);
            return Result.Success<IReadOnlyCollection<ReceivedTransaction>, Response>(transactions);
        }
        catch (Exception e)
        {
            ProcessConsolidationBatchLog.FailedToGetReceivedTransactions(_logger, e, request.TracerId, request.BatchId, e.Message);
            return Result.Failure<IReadOnlyCollection<ReceivedTransaction>, Response>(
                ProcessConsolidationBatchErrors.DatabaseError(request.TracerId, e.Message));
        }
    }

    private static Dictionary<ConsolidationKey, List<ReceivedTransaction>> GroupTransactionsByConsolidationKey(
        IReadOnlyCollection<ReceivedTransaction> transactions)
    {
        return transactions
            .GroupBy(t => new ConsolidationKey(t.UserId, t.Date.Date))
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private async Task<Result<Dictionary<ConsolidationKey, DailyBalances>, Response>> GetExistingConsolidationsAsync(
        Dictionary<ConsolidationKey, List<ReceivedTransaction>> groupedTransactions,
        ProcessConsolidationBatchCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var keys = groupedTransactions.Keys.AsEnumerable();

            var existingConsolidations = await _consolidationRepository.FindDailyConsolidationsByKeysAsync(keys, cancellationToken);
            var consolidations = existingConsolidations
                .ToDictionary(c => new ConsolidationKey(c.UserId, c.Date));

            return Result.Success<Dictionary<ConsolidationKey, DailyBalances>, Response>(consolidations);
        }
        catch (Exception e)
        {
            ProcessConsolidationBatchLog.FailedToGetDailyConsolidations(_logger, e, request.TracerId, e.Message);
            return Result.Failure<Dictionary<ConsolidationKey, DailyBalances>, Response>(
                ProcessConsolidationBatchErrors.DatabaseError(request.TracerId, e.Message));
        }
    }

    private static List<DailyBalances> ComputeUpdatedConsolidations(
        Dictionary<ConsolidationKey, List<ReceivedTransaction>> groupedTransactions,
        Dictionary<ConsolidationKey, DailyBalances> existingConsolidations)
    {
        var updated = new List<DailyBalances>();

        foreach (var (key, transactions) in groupedTransactions)
        {
            var consolidation = existingConsolidations.TryGetValue(key, out var existingConsolidation)
                ? existingConsolidation
                : new DailyBalances 
                { 
                    UserId = key.UserId, 
                    Date = key.Date,
                    ConsolidationKey = key.Value
                };

            var totalCredits = transactions
                .Where(t => t.Type == TransactionType.Credit)
                .Sum(t => t.Amount);

            var totalDebits = transactions
                .Where(t => t.Type == TransactionType.Debit)
                .Sum(t => t.Amount);
            
            consolidation.ApplyBatch(totalCredits, totalDebits, transactions.Count);
            updated.Add(consolidation);
        }

        return updated;
    }

    private async Task<Response> PersistConsolidationsAsync(
        ProcessConsolidationBatchCommand request,
        List<DailyBalances> consolidations,
        List<DateTime> processedDates,
        CancellationToken cancellationToken)
    {
        try
        {
            await _consolidationRepository.ApplyDailyConsolidationsAsync(
                consolidations,
                _transactionalPublisher.Session,
                cancellationToken);

            // Extract unique consolidation keys for cache invalidation
            var consolidationKeys = consolidations
                .Select(c => c.ConsolidationKey)
                .Distinct()
                .ToList()
                .AsReadOnly();

            var completionEvent = new DailyConsolidationUpdatedEvent(
                request.BatchId,
                request.TracerId,
                processedDates.AsReadOnly(),
                consolidationKeys);

            await _transactionalPublisher.PublishAsync(completionEvent, cancellationToken);

            ProcessConsolidationBatchLog.BatchProcessed(_logger, request.TracerId, request.BatchId, processedDates.Count);

            return Response.Ok();
        }
        catch (Exception e)
        {
            ProcessConsolidationBatchLog.FailedToPersistConsolidations(_logger, e, request.TracerId, e.Message);
            return ProcessConsolidationBatchErrors.DatabaseError(request.TracerId, e.Message);
        }
    }
}
