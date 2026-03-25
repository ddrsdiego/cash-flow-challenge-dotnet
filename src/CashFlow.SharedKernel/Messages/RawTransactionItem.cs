using System;
using CashFlow.SharedKernel.Domain.Enums;

namespace CashFlow.SharedKernel.Messages;

/// <summary>
/// Represents a single transaction received in a raw request.
/// Used in RawRequest for fast ingestion without immediate processing.
/// </summary>
public record RawTransactionItem(
    string TransactionId,
    string UserId,
    TransactionType Type,
    decimal Amount,
    Category Category,
    string Description,
    DateTime Date);
