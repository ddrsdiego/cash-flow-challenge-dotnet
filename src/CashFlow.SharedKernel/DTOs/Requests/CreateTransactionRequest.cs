using System;
using CashFlow.SharedKernel.Domain.Enums;

namespace CashFlow.SharedKernel.DTOs.Requests;

/// <summary>
/// Request DTO for creating a new financial transaction.
/// Note: UserId is NOT part of this request — it is extracted from the JWT by the API Gateway.
/// </summary>
public sealed record CreateTransactionRequest(
    TransactionType Type,
    decimal Amount,
    string Description,
    Category Category,
    DateTime Date);
