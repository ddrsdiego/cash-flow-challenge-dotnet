namespace CashFlow.Transactions.API.Application.UseCases.CreateTransaction;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using SharedKernel.Domain.Entities;
using SharedKernel.DTOs.Responses;
using SharedKernel.Interfaces;
using SharedKernel.Messages;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class CreateTransactionCommandHandler :
    IRequestHandler<CreateTransactionCommand, Response>
{
    private readonly ITransactionRepository _repository;
    private readonly ITransactionalPublisher _transactionalPublisher;
    private readonly ILogger<CreateTransactionCommandHandler> _logger;

    public CreateTransactionCommandHandler(
        ITransactionRepository repository,
        ITransactionalPublisher transactionalPublisher,
        ILogger<CreateTransactionCommandHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _transactionalPublisher = transactionalPublisher ?? throw new ArgumentNullException(nameof(transactionalPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Response> Handle(CreateTransactionCommand request, CancellationToken cancellationToken)
    {
        CreateTransactionLog.ProcessingRequest(_logger, request.TracerId);

        // ═══════════════════════════════════════════════════════════════
        // FASE 1 - VALIDAR INPUTS
        // ═══════════════════════════════════════════════════════════════
        if (request.Amount <= 0)
            return CreateTransactionErrors.InvalidAmount(request.TracerId);

        if (string.IsNullOrWhiteSpace(request.Description))
            return CreateTransactionErrors.EmptyDescription(request.TracerId);

        if (request.Description.Length > 500)
            return CreateTransactionErrors.DescriptionTooLong(request.TracerId);

        if (request.Date > DateTime.UtcNow.AddDays(1))
            return CreateTransactionErrors.InvalidDate(request.TracerId);

        try
        {
            // ═══════════════════════════════════════════════════════════
            // FASE 2 - RESOLVER DEPENDÊNCIAS
            // ═══════════════════════════════════════════════════════════
            var transaction = new Transaction
            {
                UserId = request.UserId,
                Type = request.Type,
                Amount = request.Amount,
                Description = request.Description,
                Category = request.Category,
                Date = request.Date.ToUniversalTime(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // ═══════════════════════════════════════════════════════════
            // FASE 3 - PERSISTIR (transação atômica)
            // ═══════════════════════════════════════════════════════════
            return await PersistTransactionAsync(request, transaction, cancellationToken);
        }
        catch (Exception ex)
        {
            CreateTransactionLog.UnexpectedError(_logger, ex, request.TracerId, ex.Message);
            return CreateTransactionErrors.UnexpectedError(request.TracerId, ex.Message);
        }
    }

    private async Task<Response> PersistTransactionAsync(
        CreateTransactionCommand request,
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await _transactionalPublisher.BeginTransactionAsync(cancellationToken);

            await _repository.InsertAsync(
                [transaction],
                _transactionalPublisher.Session,
                cancellationToken);

            // Generate a batch ID for this transaction batch
            var batchId = Guid.NewGuid().ToString("N");

            // Create a TransactionItem from the persisted transaction
            var transactionItem = new TransactionItem(
                transaction.Id,
                transaction.UserId,
                transaction.Type,
                transaction.Amount,
                transaction.Category,
                transaction.Date);

            // Publish the batch event with the transaction wrapped in a list
            var transactionCreatedEvent = new TransactionCreatedEvent(
                batchId,
                request.TracerId,
                new List<TransactionItem> { transactionItem });

            await _transactionalPublisher.PublishAsync(transactionCreatedEvent, cancellationToken);

            await _transactionalPublisher.CommitTransactionAsync(cancellationToken);

            CreateTransactionLog.TransactionCreated(_logger, request.TracerId, transaction.Id);

            var responseData = new TransactionResponse(
                transaction.Id,
                transaction.UserId,
                transaction.Type,
                transaction.Amount,
                transaction.Description,
                transaction.Category,
                transaction.Date,
                transaction.CreatedAt);

            return Response.Created(responseData);
        }
        catch (Exception ex)
        {
            CreateTransactionLog.FailedToPersist(_logger, ex, request.TracerId, ex.Message);
            return CreateTransactionErrors.DatabaseError(request.TracerId, ex.Message);
        }
    }
}
