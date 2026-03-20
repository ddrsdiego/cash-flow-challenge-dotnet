using System;
using Microsoft.Extensions.Logging;

namespace CashFlow.Transactions.API.Application.UseCases.ListTransactions;

internal static partial class ListTransactionsLog
{
    private const string LogType = "list-transactions";

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Listing transactions from {StartDate} to {EndDate}")]
    public static partial void ProcessingRequest(ILogger logger, string tracerId, string startDate, string endDate);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[" + LogType + "] | [{TracerId}] - Found {Count} transactions (page {Page}/{TotalPages})")]
    public static partial void TransactionsFound(ILogger logger, string tracerId, int count, int page, int totalPages);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "[" + LogType + "] | [{TracerId}] - Failed to list transactions: {Error}")]
    public static partial void FailedToListTransactions(ILogger logger, Exception exception, string tracerId, string error);
}
