using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.DTOs.Responses;
using CashFlow.SharedKernel.Interfaces;
using CSharpFunctionalExtensions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CashFlow.Transactions.API.Application.UseCases.ListTransactions;

public sealed class ListTransactionsQueryHandler : IRequestHandler<ListTransactionsQuery, Response>
{
    private readonly ITransactionRepository _repository;
    private readonly ILogger<ListTransactionsQueryHandler> _logger;

    public ListTransactionsQueryHandler(
        ITransactionRepository repository,
        ILogger<ListTransactionsQueryHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Response> Handle(ListTransactionsQuery request, CancellationToken cancellationToken)
    {
        ListTransactionsLog.ProcessingRequest(
            _logger,
            request.TracerId,
            request.StartDate.ToString("yyyy-MM-dd"),
            request.EndDate.ToString("yyyy-MM-dd"));

        // ═══════════════════════════════════════════════════════════════
        // FASE 1 - VALIDAR INPUTS
        // ═══════════════════════════════════════════════════════════════
        if (request.StartDate > request.EndDate)
            return ListTransactionsErrors.InvalidDateRange(request.TracerId);

        if (string.IsNullOrWhiteSpace(request.UserId))
            return ListTransactionsErrors.InvalidUserId(request.TracerId);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 20 : request.PageSize > 100 ? 100 : request.PageSize;

        // ═══════════════════════════════════════════════════════════════
        // FASE 2 - RESOLVER DEPENDÊNCIAS
        // ═══════════════════════════════════════════════════════════════
        var transactionsResult = await GetTransactionsByPeriodAsync(request, page, pageSize, cancellationToken);
        if (transactionsResult.IsFailure)
            return transactionsResult.Error;

        var transactions = transactionsResult.Value.Transactions;
        var totalCount = transactionsResult.Value.TotalCount;
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        ListTransactionsLog.TransactionsFound(_logger, request.TracerId, transactions.Count, page, totalPages);

        var items = transactions
            .Select(t => new TransactionResponse(
                t.Id,
                t.UserId,
                t.Type,
                t.Amount,
                t.Description,
                t.Category,
                t.Date,
                t.CreatedAt))
            .ToList();

        var pagedResult = new PagedResult<TransactionResponse>(page, pageSize, totalCount, items.AsReadOnly());

        return Response.Ok(pagedResult);
    }

    private async Task<Result<TransactionPageResult, Response>> GetTransactionsByPeriodAsync(
        ListTransactionsQuery request,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var transactions = await _repository.GetByPeriodAsync(
                request.StartDate,
                request.EndDate,
                request.Type,
                request.UserId,
                page,
                pageSize,
                cancellationToken);

            var totalCount = await _repository.CountByPeriodAsync(
                request.StartDate,
                request.EndDate,
                request.Type,
                request.UserId,
                cancellationToken);

            var result = new TransactionPageResult(transactions.ToList(), totalCount);

            return Result.Success<TransactionPageResult, Response>(result);
        }
        catch (Exception ex)
        {
            ListTransactionsLog.FailedToListTransactions(_logger, ex, request.TracerId, ex.Message);
            return Result.Failure<TransactionPageResult, Response>(
                ListTransactionsErrors.DatabaseError(request.TracerId, ex.Message));
        }
    }

    private sealed class TransactionPageResult
    {
        public List<Transaction> Transactions { get; }
        public long TotalCount { get; }

        public TransactionPageResult(
            List<Transaction> transactions,
            long totalCount)
        {
            Transactions = transactions;
            TotalCount = totalCount;
        }
    }
}
