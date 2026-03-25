using System;
using Microsoft.Extensions.Logging;

namespace CashFlow.Consolidation.Worker.Application.UseCases.ProcessConsolidationBatch;

internal static partial class ProcessConsolidationBatchLog
{
    private const string LogType = "process-consolidation-batch";

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Processing batch: {BatchId}")]
    public static partial void ProcessingBatch(ILogger logger, string tracerId, string batchId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Batch {BatchId} has no received transactions (idempotent — already processed?)")]
    public static partial void NoBatchTransactions(ILogger logger, string tracerId, string batchId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Successfully processed batch {BatchId} with {ProcessedDateCount} distinct dates")]
    public static partial void BatchProcessed(ILogger logger, string tracerId, string batchId, int processedDateCount);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to get received transactions for batch {BatchId}: {Error}")]
    public static partial void FailedToGetReceivedTransactions(ILogger logger, Exception exception, string tracerId, string batchId, string error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to get daily consolidations: {Error}")]
    public static partial void FailedToGetDailyConsolidations(ILogger logger, Exception exception, string tracerId, string error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to persist consolidations: {Error}")]
    public static partial void FailedToPersistConsolidations(ILogger logger, Exception exception, string tracerId, string error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Unexpected error: {Error}")]
    public static partial void UnexpectedError(ILogger logger, Exception exception, string tracerId, string error);
}
