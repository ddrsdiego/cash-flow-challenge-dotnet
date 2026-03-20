using System;
using Microsoft.Extensions.Logging;

namespace CashFlow.Transactions.API.Application.UseCases.GetTransactionById;

internal static partial class GetTransactionByIdLog
{
    private const string LogType = "get-transaction-by-id";

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Getting transaction: {TransactionId}")]
    public static partial void ProcessingRequest(ILogger logger, string tracerId, string transactionId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Transaction found: {TransactionId}")]
    public static partial void TransactionFound(ILogger logger, string tracerId, string transactionId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to get transaction: {Error}")]
    public static partial void FailedToGetTransaction(ILogger logger, Exception exception, string tracerId, string error);
}
