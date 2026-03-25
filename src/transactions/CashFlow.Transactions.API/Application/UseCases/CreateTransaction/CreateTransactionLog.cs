using System;
using Microsoft.Extensions.Logging;

namespace CashFlow.Transactions.API.Application.UseCases.CreateTransaction;

internal static partial class CreateTransactionLog
{
    private const string LogType = "create-transaction";

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Processing create transaction request")]
    public static partial void ProcessingRequest(ILogger logger, string tracerId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Raw request accepted for async processing")]
    public static partial void RawRequestAccepted(ILogger logger, string tracerId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Idempotent request detected (returning original response)")]
    public static partial void IdempotentRequestDetected(ILogger logger, string tracerId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to insert raw request: {Error}")]
    public static partial void FailedToInsertRawRequest(ILogger logger, Exception exception, string tracerId, string error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to persist transaction: {Error}")]
    public static partial void FailedToPersist(ILogger logger, Exception exception, string tracerId, string error);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Unexpected error: {Error}")]
    public static partial void UnexpectedError(ILogger logger, Exception exception, string tracerId, string error);
}
