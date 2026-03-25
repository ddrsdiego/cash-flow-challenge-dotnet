using System;
using Microsoft.Extensions.Logging;

namespace CashFlow.Consolidation.Worker.Consumers;

internal static partial class TransactionCreatedConsumerLog
{
    private const string LogType = "transaction-created-consumer";

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Received TransactionCreatedEvent from topic cashflow.transactions (routing: transaction.created) for batch: {BatchId}")]
    internal static partial void ReceivedEventFromTopic(ILogger logger, string tracerId, string batchId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Successfully processed batch: {BatchId}")]
    internal static partial void ProcessedSuccessfully(ILogger logger, string tracerId, string batchId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Error processing batch: {BatchId}")]
    internal static partial void ProcessingError(ILogger logger, Exception exception, string tracerId, string batchId);
}
