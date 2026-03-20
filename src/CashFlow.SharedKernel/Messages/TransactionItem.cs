using System;
using CashFlow.SharedKernel.Domain.Enums;

namespace CashFlow.SharedKernel.Messages;

/// <summary>
/// Represents a single transaction within a batch of TransactionCreatedEvent.
/// </summary>
public sealed record TransactionItem(
    string TransactionId,
    string UserId,
    TransactionType Type,
    decimal Amount,
    Category Category,
    DateTime Date);
