namespace CashFlow.Consolidation.API.Consumers;

using System;
using Microsoft.Extensions.Logging;

internal static partial class DailyConsolidationUpdatedConsumerLog
{
    private const string LogType = "daily-consolidation-updated-consumer";

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Received DailyConsolidationUpdatedEvent from topic cashflow.consolidation for batch: {BatchId}")]
    internal static partial void ReceivedEventFromTopic(ILogger logger, string tracerId, string batchId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Successfully updated cache for batch: {BatchId}")]
    internal static partial void CacheUpdatedSuccessfully(ILogger logger, string tracerId, string batchId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Error updating cache for batch: {BatchId}")]
    internal static partial void CacheUpdateError(ILogger logger, Exception exception, string tracerId, string batchId);
}
