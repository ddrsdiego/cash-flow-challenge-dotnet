using System;
using Microsoft.Extensions.Logging;

namespace CashFlow.Transactions.Worker.Application.UseCases.DispatchTransactionBatch;

internal static partial class DispatchTransactionBatchLog
{
    private const string LogType = "dispatch-transaction-batch";

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Processing command for instance {InstanceId}")]
    internal static partial void ProcessingCommand(ILogger logger, string tracerId, string instanceId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Acquired distributed lock for instance {InstanceId}")]
    internal static partial void AcquiredDistributedLock(ILogger logger, string tracerId, string instanceId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Lock not acquired (another instance holds it)")]
    internal static partial void LockNotAcquired(ILogger logger, string tracerId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Sweeping orphaned requests (threshold: {Minutes} minutes)")]
    internal static partial void SweepingOrphanedRequests(ILogger logger, string tracerId, int minutes);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Recovered {Count} orphaned requests")]
    internal static partial void OrphanedRequestsRecovered(ILogger logger, string tracerId, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - No pending requests found")]
    internal static partial void NoPendingRequests(ILogger logger, string tracerId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Found {Count} pending requests")]
    internal static partial void FoundPendingRequests(ILogger logger, string tracerId, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Batch {BatchId} dispatched with {Count} requests")]
    internal static partial void BatchDispatched(ILogger logger, string tracerId, string batchId, int count);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to acquire lock: {Error}")]
    internal static partial void FailedToAcquireLock(ILogger logger, Exception exception, string tracerId, string error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Error during orphan sweep: {Error}")]
    internal static partial void ErrorDuringSweep(ILogger logger, Exception exception, string tracerId, string error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to find pending requests: {Error}")]
    internal static partial void FailedToFindPendingRequests(ILogger logger, Exception exception, string tracerId, string error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to dispatch batch {BatchId}: {Error}")]
    internal static partial void FailedToDispatchBatch(ILogger logger, Exception exception, string tracerId, string batchId, string error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Unexpected error: {Error}")]
    internal static partial void UnexpectedError(ILogger logger, Exception exception, string tracerId, string error);
}
