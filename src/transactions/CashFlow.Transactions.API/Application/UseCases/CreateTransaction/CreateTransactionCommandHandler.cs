using CashFlow.SharedKernel.Domain.Enums;

namespace CashFlow.Transactions.API.Application.UseCases.CreateTransaction;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.DTOs.Responses;
using CashFlow.SharedKernel.Interfaces;
using CashFlow.SharedKernel.Messages;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class CreateTransactionCommandHandler :
    IRequestHandler<CreateTransactionCommand, Response>
{
    private readonly IRawRequestRepository _rawRequestRepository;
    private readonly ILogger<CreateTransactionCommandHandler> _logger;

    public CreateTransactionCommandHandler(
        IRawRequestRepository rawRequestRepository,
        ILogger<CreateTransactionCommandHandler> logger)
    {
        _rawRequestRepository = rawRequestRepository ?? throw new ArgumentNullException(nameof(rawRequestRepository));
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
            // Check for idempotency: se já existe RawRequest com a mesma chave
            // ═══════════════════════════════════════════════════════════
            var existingRequest = await _rawRequestRepository.GetByIdempotencyKeyAsync(
                request.IdempotencyKey,
                cancellationToken);

            if (existingRequest.HasValue)
            {
                CreateTransactionLog.IdempotentRequestDetected(_logger, request.TracerId);
                return Response.Accepted(
                    new CreateTransactionAcceptedResponse(
                        existingRequest.Value.Id,
                        existingRequest.Value.IdempotencyKey,
                        existingRequest.Value.Status.ToString()));
            }

            // ═══════════════════════════════════════════════════════════
            // FASE 3 - PERSISTIR (transação rápida)
            // Salva RawRequest e retorna 202 imediatamente
            // ═══════════════════════════════════════════════════════════
            return await PersistRawRequestAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            CreateTransactionLog.UnexpectedError(_logger, ex, request.TracerId, ex.Message);
            return CreateTransactionErrors.UnexpectedError(request.TracerId, ex.Message);
        }
    }

    private async Task<Response> PersistRawRequestAsync(CreateTransactionCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var rawRequest = BuildRawRequest(request);
            await _rawRequestRepository.InsertAsync(rawRequest, cancellationToken: cancellationToken);

            CreateTransactionLog.RawRequestAccepted(_logger, request.TracerId);
            return Response.Accepted(
                new CreateTransactionAcceptedResponse(
                    rawRequest.Id,
                    rawRequest.IdempotencyKey,
                    rawRequest.Status.ToString()));
        }
        catch (Exception ex)
        {
            CreateTransactionLog.FailedToInsertRawRequest(_logger, ex, request.TracerId, ex.Message);
            return CreateTransactionErrors.DatabaseError(request.TracerId, ex.Message);
        }
    }

    private static RawRequest BuildRawRequest(CreateTransactionCommand request)
    {
        var rawTransactionItem = new RawTransactionItem(
            TransactionId: Guid.NewGuid().ToString("N"),
            UserId: request.UserId,
            Type: request.Type,
            Amount: request.Amount,
            Category: request.Category,
            Description: request.Description,
            Date: request.Date.ToUniversalTime());

        return new RawRequest
        {
            IdempotencyKey = request.IdempotencyKey,
            UserId = request.UserId,
            Transactions = [rawTransactionItem],
            Status = RawRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            TracerId = request.TracerId
        };
    }
}
