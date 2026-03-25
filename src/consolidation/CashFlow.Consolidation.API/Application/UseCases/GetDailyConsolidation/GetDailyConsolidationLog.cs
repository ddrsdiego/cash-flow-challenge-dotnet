namespace CashFlow.Consolidation.API.Application.UseCases.GetDailyConsolidation;

using System;
using Microsoft.Extensions.Logging;

internal static partial class GetDailyConsolidationLog
{
    private const string LogType = "get-daily-consolidation";

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Fetching consolidation for user {UserId} on {Date:yyyy-MM-dd}")]
    internal static partial void FetchingConsolidation(ILogger logger, string tracerId, string userId, DateTime date);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Cache hit for user {UserId} on {Date:yyyy-MM-dd}")]
    internal static partial void CacheHit(ILogger logger, string tracerId, string userId, DateTime date);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Cache miss, fetching from database for user {UserId} on {Date:yyyy-MM-dd}")]
    internal static partial void CacheMiss(ILogger logger, string tracerId, string userId, DateTime date);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Successfully retrieved consolidation")]
    internal static partial void ConsolidationRetrieved(ILogger logger, string tracerId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to get consolidation from database")]
    internal static partial void FailedToGetConsolidation(ILogger logger, Exception exception, string tracerId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Unexpected error: {Error}")]
    internal static partial void UnexpectedError(ILogger logger, Exception exception, string tracerId, string error);
}
