namespace CashFlow.Consolidation.API.Application.UseCases.UpdateConsolidationCache;

using System;
using Microsoft.Extensions.Logging;

internal static partial class UpdateConsolidationCacheLog
{
    private const string LogType = "update-consolidation-cache";

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Updating cache for {KeyCount} consolidation keys")]
    internal static partial void UpdatingCache(ILogger logger, string tracerId, int keyCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Successfully updated cache: {UpdatedCount} keys updated, {InvalidatedCount} keys invalidated")]
    internal static partial void CacheUpdated(ILogger logger, string tracerId, int updatedCount, int invalidatedCount);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to get consolidations from database")]
    internal static partial void FailedToGetConsolidations(ILogger logger, Exception exception, string tracerId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to update cache: {Error}")]
    internal static partial void FailedToUpdateCache(ILogger logger, Exception exception, string tracerId, string error);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Unexpected error: {Error}")]
    internal static partial void UnexpectedError(ILogger logger, Exception exception, string tracerId, string error);
}
