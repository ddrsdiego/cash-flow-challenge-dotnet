using System;
using Microsoft.Extensions.Logging;

namespace CashFlow.Transactions.Worker.Application.UseCases.ProcessTransactionBatch;

internal static partial class ProcessTransactionBatchLog
{
    private const string LogType = "process-transaction-batch";

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Processing batch {BatchId} with {Count} raw requests")]
    public static partial void ProcessingBatch(ILogger logger, string tracerId, string batchId, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - No raw requests found for batch {BatchId}")]
    public static partial void NoRawRequestsFound(ILogger logger, string tracerId, string batchId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Batch {BatchId} processed with {Count} transactions")]
    public static partial void BatchProcessed(ILogger logger, string tracerId, string batchId, int count);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to get raw requests: {Error}")]
    public static partial void FailedToGetRawRequests(ILogger logger, Exception exception, string tracerId, string error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to persist batch {BatchId}: {Error}")]
    public static partial void FailedToPersistBatch(ILogger logger, Exception exception, string tracerId, string batchId, string error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Unexpected error: {Error}")]
    public static partial void UnexpectedError(ILogger logger, Exception exception, string tracerId, string error);
}
