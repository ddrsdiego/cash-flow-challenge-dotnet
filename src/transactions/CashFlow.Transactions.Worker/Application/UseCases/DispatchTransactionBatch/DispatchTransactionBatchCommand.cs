using MediatR;
using CashFlow.SharedKernel.Application.Utils;

namespace CashFlow.Transactions.Worker.Application.UseCases.DispatchTransactionBatch;

/// <summary>
/// Command to poll for pending raw transaction requests, acquire distributed lock, and dispatch a batch.
/// Handler encapsulates: lock acquisition, orphan sweep, pending fetch, batch dispatch.
/// Only one Batcher instance executes this via BackgroundService + distributed lock.
/// </summary>
public record DispatchTransactionBatchCommand(
    string TracerId,
    string InstanceId,
    int BatchSize,
    int LockTtlSeconds,
    int SweepThresholdMinutes) : IRequest<Response>;
