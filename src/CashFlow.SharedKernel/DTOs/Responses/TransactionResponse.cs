using System;
using CashFlow.SharedKernel.Domain.Enums;

namespace CashFlow.SharedKernel.DTOs.Responses;

/// <summary>
/// Response DTO representing a financial transaction.
/// </summary>
public sealed record TransactionResponse(
    string Id,
    string UserId,
    TransactionType Type,
    decimal Amount,
    string Description,
    Category Category,
    DateTime Date,
    DateTime CreatedAt);
