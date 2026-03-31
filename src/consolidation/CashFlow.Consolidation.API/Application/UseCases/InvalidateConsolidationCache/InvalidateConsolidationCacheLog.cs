namespace CashFlow.Consolidation.API.Application.UseCases.InvalidateConsolidationCache;

using System;
using Microsoft.Extensions.Logging;

internal static partial class InvalidateConsolidationCacheLog
{
    private const string LogType = "invalidate-consolidation-cache";

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Invalidating cache for {Count} consolidation keys")]
    internal static partial void InvalidatingCache(ILogger logger, string tracerId, int count);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Cache invalidated successfully for {Count} keys")]
    internal static partial void CacheInvalidatedSuccessfully(ILogger logger, string tracerId, int count);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to invalidate cache")]
    internal static partial void FailedToInvalidateCache(ILogger logger, Exception exception, string tracerId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Unexpected error: {Error}")]
    internal static partial void UnexpectedError(ILogger logger, Exception exception, string tracerId, string error);
}
