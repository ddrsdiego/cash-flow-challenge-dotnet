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
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Response = CashFlow.SharedKernel.Application.Utils.Response;

namespace CashFlow.Transactions.Worker.Application.UseCases.DispatchTransactionBatch;

/// <summary>
/// Handler for DispatchTransactionBatchCommand.
/// Encapsulates complete polling cycle: lock acquisition, orphan sweep, pending fetch, batch dispatch.
/// Three phases:
/// - FASE 1: Validate inputs
/// - FASE 2: Acquire lock, sweep orphaned requests, find pending requests
/// - FASE 3: Dispatch batch (mark as dispatched + publish event)
/// </summary>
public sealed class DispatchTransactionBatchCommandHandler :
    IRequestHandler<DispatchTransactionBatchCommand, Response>
{
    private const string LockId = "transaction-batcher";

    private readonly IRawRequestRepository _rawRequestRepository;
    private readonly IDistributedLockRepository _distributedLockRepository;
    private readonly IBus _bus;
    private readonly ILogger<DispatchTransactionBatchCommandHandler> _logger;

    public DispatchTransactionBatchCommandHandler(
        IRawRequestRepository rawRequestRepository,
        IDistributedLockRepository distributedLockRepository,
        IBus bus,
        ILogger<DispatchTransactionBatchCommandHandler> logger)
    {
        _rawRequestRepository = rawRequestRepository ?? throw new ArgumentNullException(nameof(rawRequestRepository));
        _distributedLockRepository = distributedLockRepository ?? throw new ArgumentNullException(nameof(distributedLockRepository));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Response> Handle(DispatchTransactionBatchCommand request, CancellationToken cancellationToken)
    {
        DispatchTransactionBatchLog.ProcessingCommand(_logger, request.TracerId, request.InstanceId);

        // ═══════════════════════════════════════════════════════════════════════════
        // FASE 1 - VALIDAR INPUTS
        // ═══════════════════════════════════════════════════════════════════════════
        if (string.IsNullOrWhiteSpace(request.TracerId))
            return DispatchTransactionBatchErrors.InvalidTracerId(request.TracerId);

        if (string.IsNullOrWhiteSpace(request.InstanceId))
            return DispatchTransactionBatchErrors.InvalidInstanceId(request.TracerId);

        try
        {
            // ═══════════════════════════════════════════════════════════════════════
            // FASE 2 - RESOLVER DEPENDÊNCIAS
            // ═══════════════════════════════════════════════════════════════════════

            // 2.1 - Acquire distributed lock
            var lockResult = await AcquireDistributedLockAsync(request, cancellationToken);
            if (lockResult.IsFailure)
                return lockResult.Error;

            // Lock not acquired (another instance holds it) — return silently
            if (!lockResult.Value.HasValue)
            {
                DispatchTransactionBatchLog.LockNotAcquired(_logger, request.TracerId);
                return Response.Ok();
            }

            DispatchTransactionBatchLog.AcquiredDistributedLock(_logger, request.TracerId, request.InstanceId);

            // 2.2 - Sweep orphaned dispatched requests (non-fatal if fails)
            await SweepOrphanedRawRequestsAsync(request, cancellationToken);

            // 2.3 - Find pending requests
            var pendingResult = await FindPendingRawRequestsAsync(request, cancellationToken);
            if (pendingResult.IsFailure)
                return pendingResult.Error;

            // No pending requests found — return silently
            if (pendingResult.Value.Count == 0)
            {
                DispatchTransactionBatchLog.NoPendingRequests(_logger, request.TracerId);
                return Response.Ok();
            }

            DispatchTransactionBatchLog.FoundPendingRequests(_logger, request.TracerId, pendingResult.Value.Count);

            // ═══════════════════════════════════════════════════════════════════════
            // FASE 3 - PERSISTIR (atômico)
            // ═══════════════════════════════════════════════════════════════════════
            return await DispatchBatchAsync(request, pendingResult.Value, cancellationToken);
        }
        catch (Exception ex)
        {
            DispatchTransactionBatchLog.UnexpectedError(_logger, ex, request.TracerId, ex.Message);
            return DispatchTransactionBatchErrors.UnexpectedError(request.TracerId, ex.Message);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // PRIVATE METHODS - FASE 2
    // ═══════════════════════════════════════════════════════════════════════════════

    private async Task<Result<Maybe<DistributedLock>, Response>> AcquireDistributedLockAsync(
        DispatchTransactionBatchCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var lockResult = await _distributedLockRepository.TryAcquireAsync(
                LockId,
                request.InstanceId,
                request.LockTtlSeconds,
                cancellationToken);

            return Result.Success<Maybe<DistributedLock>, Response>(lockResult);
        }
        catch (Exception ex)
        {
            DispatchTransactionBatchLog.FailedToAcquireLock(_logger, ex, request.TracerId, ex.Message);
            return Result.Failure<Maybe<DistributedLock>, Response>(
                DispatchTransactionBatchErrors.FailedToAcquireLock(request.TracerId, ex.Message));
        }
    }

    private async Task SweepOrphanedRawRequestsAsync(
        DispatchTransactionBatchCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            DispatchTransactionBatchLog.SweepingOrphanedRequests(_logger, request.TracerId, request.SweepThresholdMinutes);

            var orphanedRequests = await _rawRequestRepository.FindOrphanedDispatchedAsync(
                request.SweepThresholdMinutes,
                cancellationToken);

            if (orphanedRequests.Count > 0)
            {
                DispatchTransactionBatchLog.OrphanedRequestsRecovered(_logger, request.TracerId, orphanedRequests.Count);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: log and continue
            DispatchTransactionBatchLog.ErrorDuringSweep(_logger, ex, request.TracerId, ex.Message);
        }
    }

    private async Task<Result<IReadOnlyCollection<RawRequest>, Response>> FindPendingRawRequestsAsync(
        DispatchTransactionBatchCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var pendingRequests = await _rawRequestRepository.FindPendingAsync(
                request.BatchSize,
                cancellationToken);

            return Result.Success<IReadOnlyCollection<RawRequest>, Response>(pendingRequests);
        }
        catch (Exception ex)
        {
            DispatchTransactionBatchLog.FailedToFindPendingRequests(_logger, ex, request.TracerId, ex.Message);
            return Result.Failure<IReadOnlyCollection<RawRequest>, Response>(
                DispatchTransactionBatchErrors.FailedToFindPendingRequests(request.TracerId, ex.Message));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // PRIVATE METHODS - FASE 3
    // ═══════════════════════════════════════════════════════════════════════════════

    private async Task<Response> DispatchBatchAsync(
        DispatchTransactionBatchCommand request,
        IReadOnlyCollection<RawRequest> pendingRequests,
        CancellationToken cancellationToken)
    {
        try
        {
            var batchId = Guid.NewGuid().ToString("N");
            var requestIds = pendingRequests.Select(r => r.Id).ToList().AsReadOnly();

            // Mark raw requests as dispatched (atomic UpdateMany)
            await _rawRequestRepository.MarkAsDispatchedAsync(
                requestIds,
                batchId,
                cancellationToken);

            // Publish event via MassTransit Outbox
            // Outbox guarantees at-least-once delivery to transaction.batch.process queue
            var batchReadyEvent = new TransactionBatchReadyEvent(
                batchId,
                request.TracerId,
                requestIds);

            await _bus.Publish(batchReadyEvent, cancellationToken);

            DispatchTransactionBatchLog.BatchDispatched(_logger, request.TracerId, batchId, requestIds.Count);

            return Response.Accepted();
        }
        catch (Exception ex)
        {
            DispatchTransactionBatchLog.FailedToDispatchBatch(_logger, ex, request.TracerId, "", ex.Message);
            return DispatchTransactionBatchErrors.DispatchError(request.TracerId, ex.Message);
        }
    }
}
