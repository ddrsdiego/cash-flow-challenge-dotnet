using System;

namespace CashFlow.SharedKernel.DTOs.Responses;

/// <summary>
/// Response DTO representing the daily cash flow consolidation for a specific date.
/// </summary>
public sealed record DailyConsolidationResponse(
    DateTime Date,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal Balance,
    int TransactionCount,
    DateTime LastUpdated);
