using System;
using Microsoft.Extensions.Logging;

namespace CashFlow.Transactions.Worker.Consumers;

internal static partial class TransactionBatchReadyConsumerLog
{
    private const string LogType = "transaction-batch-ready-consumer";

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Received event for batch {BatchId}")]
    public static partial void ReceivedEventFromQueue(ILogger logger, string tracerId, string batchId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Successfully processed batch {BatchId}")]
    public static partial void ProcessedSuccessfully(ILogger logger, string tracerId, string batchId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Error processing batch {BatchId}")]
    public static partial void ProcessingError(ILogger logger, Exception exception, string tracerId, string batchId);
}
