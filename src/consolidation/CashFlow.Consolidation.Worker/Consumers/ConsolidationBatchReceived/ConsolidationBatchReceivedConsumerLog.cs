using Microsoft.Extensions.Logging;
using System;

namespace CashFlow.Consolidation.Worker.Consumers.ConsolidationBatchReceived;

internal static partial class ConsolidationBatchReceivedConsumerLog
{
    private const string LogType = "consolidation-batch-received-consumer";

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Received ConsolidationBatchReceivedEvent from topic cashflow.consolidation for batch: {BatchId}")]
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
