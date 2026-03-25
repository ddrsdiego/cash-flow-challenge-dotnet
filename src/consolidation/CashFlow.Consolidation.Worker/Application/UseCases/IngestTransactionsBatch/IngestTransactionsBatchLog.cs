using System;
using Microsoft.Extensions.Logging;

namespace CashFlow.Consolidation.Worker.Application.UseCases.IngestTransactionsBatch;

internal static partial class IngestTransactionsBatchLog
{
    private const string LogType = "ingest-transactions-batch";

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Processing batch: {BatchId}")]
    public static partial void ProcessingBatch(ILogger logger, string tracerId, string batchId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Successfully ingested {TransactionCount} transactions")]
    public static partial void BatchIngested(ILogger logger, string tracerId, int transactionCount);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to insert transactions: {Error}")]
    public static partial void FailedToInsertTransactions(ILogger logger, Exception exception, string tracerId, string error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to publish ConsolidationBatchReceivedEvent: {Error}")]
    public static partial void FailedToPublishEvent(ILogger logger, Exception exception, string tracerId, string error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Unexpected error: {Error}")]
    public static partial void UnexpectedError(ILogger logger, Exception exception, string tracerId, string error);
}
