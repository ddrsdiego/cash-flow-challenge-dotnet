using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.DTOs.Responses;
using CashFlow.SharedKernel.Interfaces;
using CSharpFunctionalExtensions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CashFlow.Transactions.API.Application.UseCases.GetTransactionById;

public sealed class GetTransactionByIdQueryHandler : IRequestHandler<GetTransactionByIdQuery, Response>
{
    private readonly ITransactionRepository _repository;
    private readonly ILogger<GetTransactionByIdQueryHandler> _logger;

    public GetTransactionByIdQueryHandler(
        ITransactionRepository repository,
        ILogger<GetTransactionByIdQueryHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Response> Handle(GetTransactionByIdQuery request, CancellationToken cancellationToken)
    {
        GetTransactionByIdLog.ProcessingRequest(_logger, request.TracerId, request.TransactionId);

        var transactionResult = await GetTransactionByIdAsync(request, cancellationToken);
        if (transactionResult.IsFailure)
            return transactionResult.Error;

        GetTransactionByIdLog.TransactionFound(_logger, request.TracerId, request.TransactionId);

        var t = transactionResult.Value;
        var responseData = new TransactionResponse(
            t.Id,
            t.UserId,
            t.Type,
            t.Amount,
            t.Description,
            t.Category,
            t.Date,
            t.CreatedAt);

        return Response.Ok(responseData);
    }

    private async Task<Result<Transaction, Response>> GetTransactionByIdAsync(
        GetTransactionByIdQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var maybe = await _repository.GetByIdAsync(request.TransactionId, cancellationToken);

            if (maybe.HasNoValue)
                return Result.Failure<Transaction, Response>(
                    GetTransactionByIdErrors.TransactionNotFound(request.TracerId, request.TransactionId));

            return Result.Success<Transaction, Response>(
                maybe.Value);
        }
        catch (Exception ex)
        {
            GetTransactionByIdLog.FailedToGetTransaction(_logger, ex, request.TracerId, ex.Message);
            return Result.Failure<Transaction, Response>(
                GetTransactionByIdErrors.DatabaseError(request.TracerId, ex.Message));
        }
    }
}
